using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Helix.Crawler;
using Helix.Crawler.Abstractions;
using Helix.IPC;
using Newtonsoft.Json;

namespace Helix.Gui
{
    public static class GuiController
    {
        static readonly List<Task> BackgroundTasks = new List<Task>();
        static readonly Process GuiProcess = new Process { StartInfo = { FileName = "ui/electron.exe" } };
        static readonly IpcSocket IpcSocket = new IpcSocket("127.0.0.1", 18880);
        static readonly ManualResetEvent ManualResetEvent = new ManualResetEvent(false);
        static readonly Stopwatch Stopwatch = new Stopwatch();

        [DllImport("kernel32.dll")]
        static extern IntPtr GetConsoleWindow();

        static void Main()
        {
            /* Hide the console windows.
             * TODO: Will be removed and replaced with built-in .Net Core 3.0 feature. */
            ShowWindow(GetConsoleWindow(), 0);

            IpcSocket.On("btn-start-clicked", configurationJsonString =>
            {
                if (CrawlerBot.CrawlerState != CrawlerState.Ready) return;
                var configurations = new Configurations(configurationJsonString);
                RedrawGui("Initializing ...");
                CrawlerBot.OnStopped += everythingIsDone =>
                {
                    RedrawGui(everythingIsDone ? "Done." : "Stopped.");
                    Stopwatch.Stop();
                };
                CrawlerBot.OnResourceVerified += verificationResult => Task.Run(() =>
                {
                    RedrawGui($"{verificationResult.HttpStatusCode} - {verificationResult.RawResource.Url}");
                }, CrawlerBot.CancellationToken);
                CrawlerBot.OnExceptionOccurred += exception => { RedrawGui(exception.Message); };
                CrawlerBot.StartWorking(configurations);
                RedrawGuiEvery(TimeSpan.FromSeconds(1));
                Stopwatch.Restart();
            });
            IpcSocket.On("btn-close-clicked", _ =>
            {
                RedrawGui("Shutting down ...");
                CrawlerBot.StopWorking();
                Task.WhenAll(BackgroundTasks).Wait();
                ManualResetEvent.Set();
            });
            GuiProcess.Start();

            ManualResetEvent.WaitOne();
            Stopwatch.Stop();
            IpcSocket.Dispose();
            GuiProcess.Close();
        }

        static void RedrawGui(string statusText = null)
        {
            IpcSocket.Send(new IpcMessage
            {
                Text = "redraw",
                Payload = JsonConvert.SerializeObject(new ViewModel
                {
                    CrawlerState = CrawlerBot.CrawlerState,
                    VerifiedUrlCount = null,
                    ValidUrlCount = null,
                    BrokenUrlCount = null,
                    RemainingUrlCount = CrawlerBot.RemainingUrlCount,
                    ElapsedTime = Stopwatch.Elapsed.ToString("hh' : 'mm' : 'ss"),
                    StatusText = statusText
                })
            });
        }

        static void RedrawGuiEvery(TimeSpan timeSpan)
        {
            BackgroundTasks.Add(Task.Run(() =>
            {
                while (CrawlerBot.CrawlerState != CrawlerState.Ready)
                {
                    RedrawGui();
                    Thread.Sleep(timeSpan);
                }
            }));
        }

        [DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    }
}