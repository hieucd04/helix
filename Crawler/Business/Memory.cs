using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Helix.Core;
using Helix.Crawler.Abstractions;
using JetBrains.Annotations;

namespace Helix.Crawler
{
    public sealed class Memory : IMemory
    {
        readonly ConcurrentSet<string> _alreadyVerifiedUrls = new ConcurrentSet<string>();
        readonly CancellationTokenSource _cancellationTokenSource;
        readonly BlockingCollection<HtmlDocument> _toBeExtractedHtmlDocuments = new BlockingCollection<HtmlDocument>(1000);
        readonly BlockingCollection<Uri> _toBeRenderedUris = new BlockingCollection<Uri>(1000);
        readonly BlockingCollection<RawResource> _toBeVerifiedRawResources = new BlockingCollection<RawResource>(1000);
        readonly string _workingDirectory = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
        static readonly object SyncRoot = new object();

        public Configurations Configurations { get; }

        public CrawlerState CrawlerState { get; private set; } = CrawlerState.Ready;

        public string ErrorFilePath { get; }

        public CancellationToken CancellationToken => _cancellationTokenSource.Token;

        public bool NothingLeftToDo => !_toBeVerifiedRawResources.Any() && !_toBeRenderedUris.Any() && !_toBeExtractedHtmlDocuments.Any();

        public int RemainingUrlCount => _toBeVerifiedRawResources.Count;

        public Memory(Configurations configurations)
        {
            Configurations = configurations;
            ErrorFilePath = Path.Combine(_workingDirectory, "errors.txt");
            _cancellationTokenSource = new CancellationTokenSource();
            _alreadyVerifiedUrls.Clear();
            _alreadyVerifiedUrls.Add(Configurations.StartUri.AbsoluteUri);
            _toBeVerifiedRawResources.Add(
                new RawResource
                {
                    ParentUri = null,
                    Url = Configurations.StartUri.AbsoluteUri
                },
                CancellationToken
            );
        }

        [UsedImplicitly]
        public Memory() { }

        public void CancelEverything() { _cancellationTokenSource.Cancel(); }

        public void Memorize(RawResource toBeVerifiedRawResource)
        {
            if (CancellationToken.IsCancellationRequested) return;
            lock (SyncRoot)
            {
                if (_alreadyVerifiedUrls.Contains(toBeVerifiedRawResource.Url.StripFragment())) return;
                _alreadyVerifiedUrls.Add(toBeVerifiedRawResource.Url.StripFragment());
            }

            try { _toBeVerifiedRawResources.Add(toBeVerifiedRawResource, CancellationToken); }
            catch (OperationCanceledException operationCanceledException)
            {
                if (operationCanceledException.CancellationToken != CancellationToken) throw;
            }
        }

        public void Memorize(Uri toBeRenderedUri)
        {
            try { _toBeRenderedUris.Add(toBeRenderedUri, CancellationToken); }
            catch (OperationCanceledException operationCanceledException)
            {
                if (operationCanceledException.CancellationToken != CancellationToken) throw;
            }
        }

        public void Memorize(HtmlDocument toBeExtractedHtmlDocument)
        {
            try { _toBeExtractedHtmlDocuments.Add(toBeExtractedHtmlDocument, CancellationToken); }
            catch (OperationCanceledException operationCanceledException)
            {
                if (operationCanceledException.CancellationToken != CancellationToken) throw;
            }
        }

        public HtmlDocument TakeToBeExtractedHtmlDocument() { return _toBeExtractedHtmlDocuments.Take(CancellationToken); }

        public Uri TakeToBeRenderedUri() { return _toBeRenderedUris.Take(CancellationToken); }

        public RawResource TakeToBeVerifiedRawResource() { return _toBeVerifiedRawResources.Take(CancellationToken); }

        public bool TryTransitTo(CrawlerState crawlerState)
        {
            if (CrawlerState == CrawlerState.Unknown) return false;
            switch (crawlerState)
            {
                case CrawlerState.Ready:
                    lock (SyncRoot)
                    {
                        if (CrawlerState != CrawlerState.Stopping) return false;
                        CrawlerState = CrawlerState.Ready;
                        return true;
                    }
                case CrawlerState.Working:
                    lock (SyncRoot)
                    {
                        if (CrawlerState != CrawlerState.Ready && CrawlerState != CrawlerState.Paused) return false;
                        CrawlerState = CrawlerState.Working;
                        return true;
                    }
                case CrawlerState.Stopping:
                    lock (SyncRoot)
                    {
                        if (CrawlerState != CrawlerState.Working && CrawlerState != CrawlerState.Paused) return false;
                        CrawlerState = CrawlerState.Stopping;
                        return true;
                    }
                case CrawlerState.Paused:
                    lock (SyncRoot)
                    {
                        if (CrawlerState != CrawlerState.Working) return false;
                        CrawlerState = CrawlerState.Paused;
                        return true;
                    }
                case CrawlerState.Unknown:
                    throw new NotSupportedException($"Cannot transit to [{nameof(CrawlerState.Unknown)}] state.");
                default:
                    throw new ArgumentOutOfRangeException(nameof(crawlerState), crawlerState, null);
            }
        }

        ~Memory()
        {
            _cancellationTokenSource?.Dispose();
            _toBeVerifiedRawResources?.Dispose();
        }
    }
}