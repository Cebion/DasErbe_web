using System;
using System.Runtime.InteropServices.JavaScript;
using Game.Shared.Host;
using Game.Shared.Host.Rendering;
using Game.Shared.Rendering;

namespace Game.Web;

/// <summary>
///     Presents the game's RGBA screen to an HTML canvas via one bulk-buffer JSImport call per frame (validated
///     in the render-backend spike). Screen.PresentSurface is always PixelFormat.Rgba32, so no format conversion
///     is needed - unlike Game.Desktop's MonoGameScreenPresenter, there is no GPU texture/SpriteBatch involved,
///     just a full-buffer copy handed to JS for a single putImageData call. Dirty-region partial uploads are left
///     as a later optimization; a full 320x200 buffer copy every frame is already comfortably cheap (confirmed by
///     the spike hitting ~56fps painting a full 320x240 buffer from a synthetic per-pixel loop, more expensive
///     than a plain array copy).
/// </summary>
public sealed partial class WebRenderBackend : IRenderBackend
{
    private byte[]? _uploadBuffer;

    /// <inheritdoc />
    public void Dispose()
    {
    }

    /// <inheritdoc />
    public void Present(Screen screen, HostPresentationRect rect)
    {
        var pixels = screen.PresentSurface.GetReadOnlyPixelSpan();
        if (_uploadBuffer is null || _uploadBuffer.Length != pixels.Length)
        {
            _uploadBuffer = new byte[pixels.Length];
        }

        pixels.CopyTo(_uploadBuffer);
        Paint(screen.Width, screen.Height, _uploadBuffer);
    }

    [JSImport("canvas.paint", "main.js")]
    private static partial void Paint(int width, int height, [JSMarshalAs<JSType.MemoryView>] Span<byte> pixels);
}
