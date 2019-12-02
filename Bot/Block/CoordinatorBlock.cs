﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Helix.Bot.Abstractions;
using Helix.Core;
using log4net;
using Newtonsoft.Json;

namespace Helix.Bot
{
    public class CoordinatorBlock : TransformManyBlock<ProcessingResult, Resource>, ICoordinatorBlock, IDisposable
    {
        readonly StateMachine<WorkflowState, WorkflowCommand> _stateMachine;

        public BufferBlock<Event> Events { get; }

        public override Task Completion => Task.WhenAll(base.Completion, Events.Completion);

        public CoordinatorBlock(IProcessedUrlRegister processedUrlRegister, IStatistics statistics, IResourceScope resourceScope, ILog log,
            IIncrementalIdGenerator incrementalIdGenerator)
        {
            _log = log;
            _statistics = statistics;
            _resourceScope = resourceScope;
            _processedUrlRegister = processedUrlRegister;
            _incrementalIdGenerator = incrementalIdGenerator;

            _stateMachine = NewStateMachine();
            Events = new BufferBlock<Event>(new DataflowBlockOptions { EnsureOrdered = true });

            #region Local Functions

            StateMachine<WorkflowState, WorkflowCommand> NewStateMachine()
            {
                return new StateMachine<WorkflowState, WorkflowCommand>(
                    new Dictionary<Transition<WorkflowState, WorkflowCommand>, WorkflowState>
                    {
                        { Transition(WorkflowState.WaitingForActivation, WorkflowCommand.Activate), WorkflowState.Activated },
                        { Transition(WorkflowState.Activated, WorkflowCommand.Deactivate), WorkflowState.WaitingForActivation }
                    },
                    WorkflowState.WaitingForActivation
                );

                #region Local Functions

                Transition<WorkflowState, WorkflowCommand> Transition(WorkflowState fromState, WorkflowCommand command)
                {
                    return new Transition<WorkflowState, WorkflowCommand>(fromState, command);
                }

                #endregion
            }

            #endregion
        }

        public override void Complete()
        {
            base.Complete();
            TryReceiveAll(out _);

            base.Completion.Wait();
            Events.Complete();
        }

        public void Dispose() { _stateMachine?.Dispose(); }

        public bool TryActivateWorkflow(string startUrl)
        {
            var activationSuccessful = false;
            var stateTransitionSucceeded = _stateMachine.TryTransitNext(WorkflowCommand.Activate, () =>
            {
                try
                {
                    _statistics.IncrementRemainingWorkload();
                    activationSuccessful = this.Post(new SuccessfulProcessingResult
                    {
                        NewResources = new List<Resource>
                        {
                            new Resource(_incrementalIdGenerator.GetNext(), startUrl, null, true)
                            {
                                IsInternal = true
                            }
                        }
                    });

                    if (!activationSuccessful) throw new ArgumentException("Could not activate workflow using given start URL", startUrl);
                }
                catch (Exception exception)
                {
                    _statistics.DecrementRemainingWorkload();
                    _log.Error("One or more errors occurred when trying to activate workflow.", exception);

                    if (!_stateMachine.TryTransitNext(WorkflowCommand.Deactivate))
                        _log.StateTransitionFailureEvent(_stateMachine.CurrentState, WorkflowCommand.Deactivate);
                }
            });
            if (!stateTransitionSucceeded) _log.StateTransitionFailureEvent(_stateMachine.CurrentState, WorkflowCommand.Activate);

            return activationSuccessful;
        }

        protected override IEnumerable<Resource> Transform(ProcessingResult processingResult)
        {
            try
            {
                if (processingResult == null)
                    throw new ArgumentNullException(nameof(processingResult));

                var processedResource = processingResult.ProcessedResource;
                var isNotInitialProcessingResult = processedResource != null;
                if (isNotInitialProcessingResult)
                {
                    var redirectHappened = !processedResource.OriginalUri.Equals(processedResource.Uri);
                    if (_resourceScope.IsStartUri(processedResource.OriginalUri) && redirectHappened)
                    {
                        SendOut(new RedirectHappenedAtStartUrlEvent { FinalUrlAfterRedirects = processedResource.Uri.AbsoluteUri });
                        return null;
                    }

                    if (!_processedUrlRegister.IsRegistered(processedResource.Uri.AbsoluteUri))
                        _log.Error($"Processed resource was not registered by {nameof(CoordinatorBlock)}.");
                }

                var newlyDiscoveredResources = DiscoverNewResources();
                _statistics.DecrementRemainingWorkload();

                var remainingWorkload = _statistics.TakeSnapshot().RemainingWorkload;
                SendOut(new ResourceProcessedEvent { RemainingWorkload = remainingWorkload });
                if (remainingWorkload > 0) return newlyDiscoveredResources;

                SendOut(new NoMoreWorkToDoEvent());
                return new List<Resource>();

                #region Local Functions

                List<Resource> DiscoverNewResources()
                {
                    if (!(processingResult is SuccessfulProcessingResult successfulProcessingResult)) return new List<Resource>();
                    return successfulProcessingResult.NewResources.Where(newResource =>
                    {
                        var resourceWasNotProcessed = _processedUrlRegister.TryRegister(newResource.Uri.AbsoluteUri);
                        if (resourceWasNotProcessed) _statistics.IncrementRemainingWorkload();
                        return resourceWasNotProcessed;
                    }).ToList();
                }
                void SendOut(Event @event)
                {
                    if (!Events.Post(@event) && !Events.Completion.IsCompleted)
                        _log.Error($"Failed to post data to buffer block named [{nameof(Events)}].");
                }

                #endregion
            }
            catch (Exception exception)
            {
                _log.Error($"One or more errors occurred while coordinating: {JsonConvert.SerializeObject(processingResult)}.", exception);
                return null;
            }
        }

        #region Injected Services

        readonly ILog _log;
        readonly IStatistics _statistics;
        readonly IResourceScope _resourceScope;
        readonly IProcessedUrlRegister _processedUrlRegister;
        readonly IIncrementalIdGenerator _incrementalIdGenerator;

        #endregion
    }
}