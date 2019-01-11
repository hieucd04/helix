using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Helix.Crawler.Abstractions;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using Titanium.Web.Proxy;
using Titanium.Web.Proxy.EventArguments;
using Titanium.Web.Proxy.Models;

namespace Helix.Crawler
{
    public sealed class LegacyResourceCollector
    {
        const int HttpProxyPort = 18882;
        readonly ChromeDriver _chromeDriver;
        bool _disposed;
        static ProxyServer _httpProxyServer;
        readonly IResourceScope _resourceScope;
        static readonly object StaticLock = new object();

        public event AllAttemptsToCollectNewRawResourcesFailedEvent OnAllAttemptsToCollectNewRawResourcesFailed;
        public event BrowserExceptionOccurredEvent OnBrowserExceptionOccurred;
        public event IdleEvent OnIdle;
        public event RawResourceCollectedEvent OnRawResourceCollected;

        public LegacyResourceCollector(Configurations configurations, IResourceScope resourceScope)
        {
            _resourceScope = resourceScope;
            SetupHttpProxyServer();

            var workingDirectory = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
            var chromeDriverService = ChromeDriverService.CreateDefaultService(workingDirectory);
            chromeDriverService.HideCommandPromptWindow = true;

            var chromeOptions = new ChromeOptions();
            if (!configurations.ShowWebBrowsers) chromeOptions.AddArguments("--headless", "--incognito");
            chromeOptions.BinaryLocation = Path.Combine(workingDirectory, "chromium/chrome.exe");
            chromeOptions.Proxy = new Proxy
            {
                HttpProxy = $"http://localhost:{HttpProxyPort}",
                SslProxy = $"http://localhost:{HttpProxyPort}",
                FtpProxy = $"http://localhost:{HttpProxyPort}"
            };

            _chromeDriver = new ChromeDriver(chromeDriverService, chromeOptions);
        }

        public void CollectNewRawResourcesFrom(Resource parentResource)
        {
            try
            {
                _chromeDriver.Navigate().GoToUrl(parentResource.Uri);
                var hrefSchemeIsSupported = new Func<string, bool>(href => href.StartsWith("http", StringComparison.OrdinalIgnoreCase) ||
                                                                           href.StartsWith("https", StringComparison.OrdinalIgnoreCase) ||
                                                                           href.StartsWith("/", StringComparison.OrdinalIgnoreCase));
                var newRawResources = TryGetUrls("a", "href")
                    .Where(hrefSchemeIsSupported)
                    .Select(href => new RawResource { ParentUri = parentResource.Uri, Url = href });
                Parallel.ForEach(newRawResources, newRawResource => { OnRawResourceCollected?.Invoke(newRawResource); });
            }
            catch (WebDriverException webDriverException)
            {
                OnBrowserExceptionOccurred?.Invoke(webDriverException, parentResource);
            }
            catch (TaskCanceledException)
            {
                OnAllAttemptsToCollectNewRawResourcesFailed?.Invoke(parentResource);
            }
            finally
            {
                OnIdle?.Invoke();
            }
        }

        public void Dispose()
        {
            ReleaseUnmanagedResources();
            GC.SuppressFinalize(this);
        }

        async Task CaptureNetworkTraffic(object _, SessionEventArgs networkTraffic)
        {
            await Task.Run(() =>
            {
                var response = networkTraffic.WebSession.Response;
                if (response.ContentType == null) return;

                var request = networkTraffic.WebSession.Request;
                var isNotGETRequest = request.Method.ToUpperInvariant() != "GET";
                var isNotCss = !response.ContentType.StartsWith("text/css", StringComparison.OrdinalIgnoreCase);
                var isNotImage = !response.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
                var isNotAudio = !response.ContentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase);
                var isNotVideo = !response.ContentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase);
                var isNotFont = !response.ContentType.StartsWith("font/", StringComparison.OrdinalIgnoreCase);
                var isNotJavaScript = !response.ContentType.StartsWith("application/javascript", StringComparison.OrdinalIgnoreCase) &&
                                      !response.ContentType.StartsWith("application/ecmascript", StringComparison.OrdinalIgnoreCase);
                if (isNotGETRequest || isNotCss && isNotFont && isNotJavaScript && isNotImage && isNotAudio && isNotVideo) return;
                var newRawResource = new RawResource
                {
                    ParentUri = new Uri(request.OriginalUrl),
                    Url = request.Url,
                    HttpStatusCode = response.StatusCode
                };
                OnRawResourceCollected?.Invoke(newRawResource);
            });
        }

        async Task EnsureInternal(object _, SessionEventArgs networkTraffic)
        {
            await Task.Run(() =>
            {
                networkTraffic.WebSession.Request.RequestUri = _resourceScope.Localize(networkTraffic.WebSession.Request.RequestUri);
                networkTraffic.WebSession.Request.Host = networkTraffic.WebSession.Request.RequestUri.Host;
            });
        }

        void ReleaseUnmanagedResources()
        {
            if (_disposed) return;
            _disposed = true;
            _chromeDriver?.Quit();
            _httpProxyServer?.Stop();
            _httpProxyServer?.Dispose();
            _httpProxyServer = null;
        }

        void SetupHttpProxyServer()
        {
            lock (StaticLock)
            {
                if (_httpProxyServer != null) return;
                _httpProxyServer = new ProxyServer();
                _httpProxyServer.AddEndPoint(new ExplicitProxyEndPoint(IPAddress.Any, HttpProxyPort));
                _httpProxyServer.Start();
                _httpProxyServer.BeforeRequest += EnsureInternal;
                _httpProxyServer.BeforeResponse += CaptureNetworkTraffic;
            }
        }

        IEnumerable<string> TryGetUrls(string tagName, string attributeName)
        {
            var stopWatch = new Stopwatch();
            stopWatch.Start();

            while (stopWatch.Elapsed.TotalSeconds < 30)
            {
                try
                {
                    var urls = new List<string>();
                    foreach (var webElement in _chromeDriver.FindElementsByTagName(tagName))
                    {
                        var url = webElement.GetAttribute(attributeName);
                        if (!string.IsNullOrWhiteSpace(url)) urls.Add(url);
                    }
                    return urls;
                }
                catch (StaleElementReferenceException) { }
                Thread.Sleep(1000);
            }

            stopWatch.Reset();
            throw new TaskCanceledException();
        }

        ~LegacyResourceCollector() { ReleaseUnmanagedResources(); }
    }
}