using System;
using System.Threading;
using System.Threading.Tasks;
using Game.Hosting;
using Game.Input;
using Game.Runtime;
using Game.Shared.Host;
using Game.Shared.Host.Rendering;
using Game.Shared.Input;
using Game.Shared.Rendering;

namespace Game.Web;

/// <summary>
///     Runs the web host: same dedicated-background-thread/ThreadPump/triple-buffer model as
///     Game.Desktop's DesktopGameHost, but driven by a browser requestAnimationFrame tick instead of
///     MonoGame's Update/Draw callbacks. This is the core claim the feasibility spike validated - the
///     unmodified blocking-wait ThreadPump genuinely runs under WasmEnableThreads + coi-serviceworker.
///     Letterboxing/pointer-bounds math is intentionally skipped: the canvas element IS the content rect
///     (sized to the screen's native resolution, scaled via CSS), and WebInputBackend gets its pointer bounds
///     pushed directly from JS mouse events instead of computing them from a viewport/screen fit like
///     Game.Desktop's MonoGameScreenPresenter.ComputePresentationRect does, so HostPresentationRect.Empty is
///     passed through here as an unused placeholder.
/// </summary>
internal sealed class WebGameHost : IDisposable
{
    private readonly Erbe _runtime;
    private readonly WebInputBackend _inputBackend;
    private readonly Lock _runtimeGate = new();
    private readonly ManualResetEventSlim _bootCompleted = new(false);
    private readonly ThreadPump _threadPump = new();
    private WebRenderBackend? _renderBackend;
    private WebMusicPlayer? _musicPlayer;
    private Thread? _runtimeLoopThread;
    private Exception? _runtimeLoopFailure;
    private Screen[]? _presentationBuffers;
    private Screen? _frontBuffer;
    private Screen? _inFlightBuffer;
    private bool _isFrontBufferFresh;

    public WebGameHost(Erbe runtime, WebInputBackend inputBackend)
    {
        _runtime = runtime;
        _inputBackend = inputBackend;
    }

    /// <summary>
    ///     Attaches host services and starts the background runtime loop, returning once the first frame is ready.
    /// </summary>
    public async Task StartAsync()
    {
        _renderBackend = new WebRenderBackend();
        _musicPlayer = new WebMusicPlayer();
        _runtime.AttachRenderBackend(_renderBackend);
        _runtime.AttachMusicPlayer(_musicPlayer);

        _threadPump.ConfigureFramePreparedCallback(OnRuntimeFramePrepared);
        var initialInput = _inputBackend.Poll(HostPresentationRect.Empty);
        _threadPump.SubmitHostSlice(initialInput, TimeSpan.Zero);

        _runtimeLoopThread = new Thread(RunRuntimeLoop)
        {
            IsBackground = true,
            Name = "WebGameRuntimeLoop"
        };
        _runtimeLoopThread.Start();

        // Wait for the first published frame off the browser's main thread so the tab stays responsive.
        await Task.Run(() => _bootCompleted.Wait()).ConfigureAwait(false);
        ThrowIfRuntimeLoopFailed();
    }

    /// <summary>
    ///     Advances one host frame: polls input, submits the elapsed time slice to the background game thread,
    ///     and presents the latest published frame. Called once per requestAnimationFrame callback.
    /// </summary>
    /// <param name="elapsed">Elapsed host time to accumulate since the previous tick.</param>
    public void Tick(TimeSpan elapsed)
    {
        ThrowIfRuntimeLoopFailed();

        var input = _inputBackend.Poll(HostPresentationRect.Empty);
        if (_runtime.IsPaused)
        {
            _threadPump.SubmitHostSlice(InputFrame.Empty, TimeSpan.Zero);
        }
        else
        {
            _threadPump.SubmitHostSlice(input, elapsed);
        }

        Screen? presentationSnapshot;
        lock (_runtimeGate)
        {
            presentationSnapshot = _frontBuffer;
            _inFlightBuffer = presentationSnapshot;
        }

        if (presentationSnapshot is null || _runtime.RenderBackend is null)
        {
            return;
        }

        try
        {
            _runtime.RenderBackend.Present(presentationSnapshot, HostPresentationRect.Empty);
        }
        finally
        {
            lock (_runtimeGate)
            {
                if (ReferenceEquals(presentationSnapshot, _frontBuffer) && _isFrontBufferFresh)
                {
                    presentationSnapshot.ClearDirty();
                    _isFrontBufferFresh = false;
                }

                if (ReferenceEquals(presentationSnapshot, _inFlightBuffer))
                {
                    _inFlightBuffer = null;
                }
            }
        }
    }

    public void Dispose()
    {
        var runtimeLoopThread = _runtimeLoopThread;
        if (runtimeLoopThread is not null)
        {
            _threadPump.RequestStop();
            runtimeLoopThread.Join();
            _runtimeLoopThread = null;
        }

        _threadPump.Dispose();
        _runtime.Music.Dispose();
        _runtime.RenderBackend?.Dispose();
        _musicPlayer?.Dispose();
        _renderBackend?.Dispose();
        _bootCompleted.Dispose();
    }

    private void RunRuntimeLoop()
    {
        try
        {
            _runtime.HostPacing.Attach(_threadPump, RenderFrame, _threadPump.StopToken);
            try
            {
                _runtime.HostPacing.PublishInitialInput();
                _runtime.Bootstrap.Run();
            }
            catch (OperationCanceledException) when (_threadPump.StopToken.IsCancellationRequested)
            {
            }
            finally
            {
                _runtime.HostPacing.Detach();
                _threadPump.NotifyRuntimeLoopStopped();
            }
        }
        catch (Exception ex)
        {
            lock (_runtimeGate)
            {
                _runtimeLoopFailure = ex;
            }

            _bootCompleted.Set();
        }
    }

    private void RenderFrame()
    {
        _runtime.ScreenComposer.ComposeFrame();
    }

    private void OnRuntimeFramePrepared()
    {
        lock (_runtimeGate)
        {
            var liveScreen = _runtime.State.Presentation.Screen;
            var snapshotRecreated = EnsurePresentationBuffers(liveScreen);
            var backBuffer = ResolvePresentationWriteBuffer();
            backBuffer.ClearDirty();
            if (snapshotRecreated || liveScreen.DirtyRegions.IsFull)
            {
                liveScreen.PresentSurface.GetReadOnlyPixelSpan().CopyTo(backBuffer.PresentSurface.GetPixelSpan());
                backBuffer.InvalidateAll();
            }
            else
            {
                foreach (var region in liveScreen.DirtyRegions.Regions)
                {
                    Blitter.Copy(liveScreen.PresentSurface, region, backBuffer.PresentSurface, region.X, region.Y);
                    backBuffer.Invalidate(region);
                }
            }

            liveScreen.ClearDirty();
            _frontBuffer = backBuffer;
            _isFrontBufferFresh = true;
        }

        _bootCompleted.Set();
    }

    private bool EnsurePresentationBuffers(Screen liveScreen)
    {
        if (_presentationBuffers is not null && _frontBuffer is not null && _frontBuffer.Width == liveScreen.Width &&
            _frontBuffer.Height == liveScreen.Height)
        {
            return false;
        }

        _presentationBuffers =
        [
            new Screen(liveScreen.Width, liveScreen.Height),
            new Screen(liveScreen.Width, liveScreen.Height),
            new Screen(liveScreen.Width, liveScreen.Height)
        ];
        _frontBuffer = _presentationBuffers[0];
        _inFlightBuffer = null;
        _isFrontBufferFresh = false;
        return true;
    }

    private Screen ResolvePresentationWriteBuffer()
    {
        foreach (var presentationBuffer in _presentationBuffers!)
        {
            if (!ReferenceEquals(presentationBuffer, _frontBuffer) &&
                !ReferenceEquals(presentationBuffer, _inFlightBuffer))
            {
                return presentationBuffer;
            }
        }

        throw new InvalidOperationException("No available presentation buffer remained for the runtime handoff.");
    }

    private void ThrowIfRuntimeLoopFailed()
    {
        if (_runtimeLoopFailure is not null)
        {
            throw new InvalidOperationException("The background runtime loop failed.", _runtimeLoopFailure);
        }
    }
}
