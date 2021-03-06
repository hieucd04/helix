﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Helix.Bot.Abstractions;
using log4net;

namespace Helix.Bot
{
    public class HardwareMonitor : IHardwareMonitor
    {
        CancellationTokenSource _cancellationTokenSource;
        bool _isRunning;
        readonly ILog _log;
        Task _samplingTask;

        public event Action<int?, int?> OnHighCpuOrMemoryUsage;

        public event Action<int, int> OnLowCpuAndMemoryUsage;

        [Obsolete(ErrorMessage.UseDependencyInjection, true)]
        public HardwareMonitor(ILog log) { _log = log; }

        public void StartMonitoring(double millisecondSampleDuration, float highCpuUsageThreshold, float lowCpuUsageThreshold,
            float highMemoryUsageThreshold, float lowMemoryUsageThreshold)
        {
            if (_isRunning) throw new Exception($"{nameof(HardwareMonitor)} is already running.");
            _isRunning = true;

            var cpuUsageSamples = new List<int>();
            _cancellationTokenSource = new CancellationTokenSource();
            _samplingTask = Task.Run(() =>
            {
                using var performanceCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                performanceCounter.NextValue();
                while (!_cancellationTokenSource.IsCancellationRequested)
                {
                    const int millisecondSampleInterval = 1000;
                    Thread.Sleep(millisecondSampleInterval);

                    const float bufferRate = 1.5f;
                    cpuUsageSamples.Add((int) MathF.Ceiling(performanceCounter.NextValue() * bufferRate));

                    var millisecondTotalElapsedTime = cpuUsageSamples.Count * millisecondSampleInterval;
                    if (millisecondTotalElapsedTime < millisecondSampleDuration) continue;

                    CheckCpuAndMemoryUsage();
                    cpuUsageSamples.Clear();
                }
            }, _cancellationTokenSource.Token);

            void CheckCpuAndMemoryUsage()
            {
                var averageCpuUsage = (int) Math.Ceiling(cpuUsageSamples.Average());
                var memoryUsage = GetMemoryUsage();
                var highCpuUsage = averageCpuUsage >= highCpuUsageThreshold;
                var lowCpuUsage = averageCpuUsage < lowCpuUsageThreshold;
                var highMemoryUsage = memoryUsage >= highMemoryUsageThreshold;
                var lowMemoryUsage = memoryUsage < lowMemoryUsageThreshold;

                try
                {
                    if (highCpuUsage) OnHighCpuOrMemoryUsage?.Invoke(averageCpuUsage, null);
                    else if (highMemoryUsage) OnHighCpuOrMemoryUsage?.Invoke(null, memoryUsage);
                    else if (lowCpuUsage && lowMemoryUsage) OnLowCpuAndMemoryUsage?.Invoke(averageCpuUsage, memoryUsage);
                }
                catch (Exception exception)
                {
                    _log.Error("One or more errors occurred in the sampling task.", exception);
                }
            }
        }

        public void StopMonitoring()
        {
            if (!_isRunning) throw new Exception($"{nameof(HardwareMonitor)} is not running.");
            _isRunning = false;

            _cancellationTokenSource.Cancel();
            _samplingTask.Wait();

            _cancellationTokenSource.Dispose();
            _cancellationTokenSource = null;
            _samplingTask.Dispose();
            _samplingTask = null;
        }

        int GetMemoryUsage()
        {
            var performanceInformation = new PerformanceInformation();
            if (!GetPerformanceInfo(out performanceInformation, Marshal.SizeOf(performanceInformation)))
                _log.Info($"Failed to get performance information. Default value used is: {performanceInformation}");

            var totalMemory = performanceInformation.PhysicalTotal.ToInt64();
            var consumedMemory = totalMemory - performanceInformation.PhysicalAvailable.ToInt64();
            return (int) Math.Round(100f * consumedMemory / totalMemory, 0);
        }

        [DllImport("psapi.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool GetPerformanceInfo([Out] out PerformanceInformation performanceInformation, [In] int size);

        [StructLayout(LayoutKind.Sequential)]
        [SuppressMessage("ReSharper", "MemberCanBePrivate.Local")]
        struct PerformanceInformation
        {
            public readonly int Size;
            public readonly IntPtr CommitTotal;
            public readonly IntPtr CommitLimit;
            public readonly IntPtr CommitPeak;
            public readonly IntPtr PhysicalTotal;
            public readonly IntPtr PhysicalAvailable;
            public readonly IntPtr SystemCache;
            public readonly IntPtr KernelTotal;
            public readonly IntPtr KernelPaged;
            public readonly IntPtr KernelNonPaged;
            public readonly IntPtr PageSize;
            public readonly int HandlesCount;
            public readonly int ProcessCount;
            public readonly int ThreadCount;
        }
    }
}