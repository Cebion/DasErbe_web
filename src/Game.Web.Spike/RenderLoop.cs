using System;
using System.Diagnostics;
using System.Runtime.InteropServices.JavaScript;
using System.Threading.Tasks;

internal static partial class RenderLoop
{
    private const int Width = 320;
    private const int Height = 240;

    private static readonly byte[] Pixels = new byte[Width * Height * 4];
    private static readonly Stopwatch FrameClock = Stopwatch.StartNew();
    private static long _frameCount;
    private static long _lastFpsReportMs;

    public static void Run()
    {
        _ = RunLoopAsync();
    }

    private static async Task RunLoopAsync()
    {
        while (true)
        {
            FillTestPattern(_frameCount);
            Paint(Width, Height, Pixels);
            _frameCount++;

            var nowMs = FrameClock.ElapsedMilliseconds;
            if (nowMs - _lastFpsReportMs >= 1000)
            {
                var fps = _frameCount * 1000.0 / Math.Max(1, nowMs);
                var pulses = ThreadPumpSpike.PulseAndGetCount();
                Console.WriteLine(
                    $"[spike] frame {_frameCount}, elapsed {nowMs} ms, avg fps {fps:F1}, worker pulses {pulses}");
                ReportStats(_frameCount, fps, pulses);
                _lastFpsReportMs = nowMs;
            }

            // Yield back to the browser event loop between frames instead of blocking a thread.
            await Task.Delay(16);
        }
    }

    private static void FillTestPattern(long frame)
    {
        var shift = (int)(frame % Width);
        for (var y = 0; y < Height; y++)
        {
            var rowOffset = y * Width * 4;
            for (var x = 0; x < Width; x++)
            {
                var stripe = ((x + shift) / 16 + y / 16) % 2 == 0;
                var pixelOffset = rowOffset + x * 4;
                Pixels[pixelOffset + 0] = stripe ? (byte)255 : (byte)32;   // R
                Pixels[pixelOffset + 1] = (byte)(x * 255 / Width); // G
                Pixels[pixelOffset + 2] = (byte)(y * 255 / Height); // B
                Pixels[pixelOffset + 3] = 255; // A
            }
        }
    }

    [JSImport("canvas.paint", "main.js")]
    internal static partial void Paint(int width, int height,
        [JSMarshalAs<JSType.MemoryView>] Span<byte> pixels);

    [JSImport("canvas.reportStats", "main.js")]
    internal static partial void ReportStats(
        [JSMarshalAs<JSType.Number>] long frameCount, double fps,
        [JSMarshalAs<JSType.Number>] long workerPulses);
}
