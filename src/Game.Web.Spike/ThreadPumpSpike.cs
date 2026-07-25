using System;
using System.Runtime.InteropServices.JavaScript;
using System.Threading;

// Mirrors the real Game.Core/Hosting/ThreadPump.cs shape: a dedicated background Thread paced by
// blocking WaitHandle waits, exactly the pattern that requires real WASM threads (SharedArrayBuffer)
// to run in a browser at all.
internal static partial class ThreadPumpSpike
{
    private static readonly AutoResetEvent Pulse = new(false);
    private static readonly ManualResetEventSlim Idle = new(true);
    private static long _pulseCount;
    private static string _status = "not started";

    public static void Start()
    {
        try
        {
            var thread = new Thread(RunWorker)
            {
                IsBackground = true,
                Name = "SpikeThreadPumpWorker"
            };
            thread.Start();
            _status = "thread started";
            Console.WriteLine("[spike] background Thread.Start() succeeded.");
        }
        catch (Exception ex)
        {
            _status = $"thread start FAILED: {ex.GetType().Name}: {ex.Message}";
            Console.WriteLine($"[spike] background Thread.Start() FAILED: {ex}");
        }

        ReportThreadStatus(_status);
    }

    public static long PulseAndGetCount()
    {
        Pulse.Set();
        Idle.Wait(TimeSpan.FromMilliseconds(500));
        return Interlocked.Read(ref _pulseCount);
    }

    private static void RunWorker()
    {
        while (true)
        {
            Idle.Set();
            Pulse.WaitOne();
            Idle.Reset();
            Interlocked.Increment(ref _pulseCount);
        }
    }

    [JSImport("canvas.reportThreadStatus", "main.js")]
    internal static partial void ReportThreadStatus(string status);
}
