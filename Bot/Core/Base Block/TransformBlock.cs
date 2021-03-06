using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace Helix.Bot
{
    public abstract class TransformBlock<TInput, TOutput> : IPropagatorBlock<TInput, TOutput>, IReceivableSourceBlock<TOutput>
    {
        readonly System.Threading.Tasks.Dataflow.TransformBlock<TInput, TOutput> _transformBlock;

        public virtual Task Completion => _transformBlock.Completion;

        public int InputCount => _transformBlock.InputCount;

        public int OutputCount => _transformBlock.OutputCount;

        protected TransformBlock(bool ensureOrdered = false, int maxDegreeOfParallelism = 1)
        {
            _transformBlock = new System.Threading.Tasks.Dataflow.TransformBlock<TInput, TOutput>(
                input => Transform(input),
                new ExecutionDataflowBlockOptions
                {
                    EnsureOrdered = ensureOrdered,
                    MaxDegreeOfParallelism = maxDegreeOfParallelism
                }
            );
        }

        public virtual void Complete() { _transformBlock.Complete(); }

        public TOutput ConsumeMessage(DataflowMessageHeader messageHeader, ITargetBlock<TOutput> target, out bool messageConsumed)
        {
            return ((ISourceBlock<TOutput>) _transformBlock).ConsumeMessage(messageHeader, target, out messageConsumed);
        }

        public void Fault(Exception exception) { ((ISourceBlock<TOutput>) _transformBlock).Fault(exception); }

        public IDisposable LinkTo(ITargetBlock<TOutput> target, DataflowLinkOptions linkOptions)
        {
            return _transformBlock.LinkTo(target, linkOptions);
        }

        public DataflowMessageStatus OfferMessage(DataflowMessageHeader messageHeader, TInput messageValue, ISourceBlock<TInput> source,
            bool consumeToAccept)
        {
            return ((ITargetBlock<TInput>) _transformBlock).OfferMessage(messageHeader, messageValue, source, consumeToAccept);
        }

        public void ReleaseReservation(DataflowMessageHeader messageHeader, ITargetBlock<TOutput> target)
        {
            ((ISourceBlock<TOutput>) _transformBlock).ReleaseReservation(messageHeader, target);
        }

        public bool ReserveMessage(DataflowMessageHeader messageHeader, ITargetBlock<TOutput> target)
        {
            return ((ISourceBlock<TOutput>) _transformBlock).ReserveMessage(messageHeader, target);
        }

        public bool TryReceive(Predicate<TOutput> filter, out TOutput item) { return _transformBlock.TryReceive(filter, out item); }

        public bool TryReceiveAll(out IList<TOutput> items) { return _transformBlock.TryReceiveAll(out items); }

        protected abstract TOutput Transform(TInput input);
    }
}