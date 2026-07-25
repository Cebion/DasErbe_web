using System;
using System.Runtime.InteropServices.JavaScript;
using System.Threading.Tasks;

namespace Game.Web;

/// <summary>
///     Entry points JS calls into: once to boot the runtime after preloading assets, then once per
///     requestAnimationFrame to advance and present a frame.
/// </summary>
internal static partial class GameApp
{
    private static WebGameHost? _host;
    private static double _lastTimestampMs = -1;

    /// <summary>
    ///     Preloads game assets, constructs the runtime, and starts the background game loop. Reports
    ///     success/failure back to JS so the page can show a real error instead of a blank canvas.
    /// </summary>
    [JSExport]
    internal static async Task Boot()
    {
        try
        {
            ReportStatus("Downloading game assets...");
            var assetRoot = await WebAssetPreloader.PreloadAsync().ConfigureAwait(false);

            var inputBackend = new WebInputBackend();
            if (!WebRuntimeFactory.TryCreate(assetRoot, inputBackend, out var runtime, out var errorMessage))
            {
                ReportStatus($"Failed to start: {errorMessage}");
                return;
            }

            ReportStatus("Starting runtime...");
            _host = new WebGameHost(runtime, inputBackend);
            await _host.StartAsync().ConfigureAwait(false);
            ReportStatus("Running.");
        }
        catch (Exception ex)
        {
            ReportStatus($"Failed to start: {ex}");
        }
    }

    /// <summary>
    ///     Advances one frame. Called from a requestAnimationFrame loop in JS with the callback's own
    ///     high-resolution timestamp (milliseconds).
    /// </summary>
    /// <param name="timestampMs">The requestAnimationFrame callback's high-resolution timestamp, in milliseconds.</param>
    /// <remarks>
    ///     Returns <see cref="Task" /> rather than <see langword="void" /> because under WasmEnableThreads a
    ///     synchronous (void-returning) [JSExport] throws "Cannot call synchronous C# methods" at runtime -
    ///     confirmed on a real deployed build. No actual awaiting happens inside.
    /// </remarks>
    [JSExport]
    internal static Task Tick(double timestampMs)
    {
        if (_host is null)
        {
            return Task.CompletedTask;
        }

        if (_lastTimestampMs < 0)
        {
            _lastTimestampMs = timestampMs;
        }

        var elapsed = TimeSpan.FromMilliseconds(Math.Max(0, timestampMs - _lastTimestampMs));
        _lastTimestampMs = timestampMs;

        try
        {
            _host.Tick(elapsed);
        }
        catch (Exception ex)
        {
            ReportStatus($"Runtime error: {ex}");
            throw;
        }

        return Task.CompletedTask;
    }

    [JSImport("app.reportStatus", "main.js")]
    private static partial void ReportStatus(string status);
}
