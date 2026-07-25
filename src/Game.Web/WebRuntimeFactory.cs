using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using Game.Runtime;
using Game.Shared.Resources.Management;

namespace Game.Web;

/// <summary>
///     Creates the runtime used by the web host, mirroring Game.Desktop's DesktopRuntimeFactory but resolving
///     assets from the browser-local path WebAssetPreloader populated instead of the real filesystem.
/// </summary>
internal static class WebRuntimeFactory
{
    /// <summary>
    ///     Tries to create the runtime against an already-preloaded asset root.
    /// </summary>
    /// <param name="assetRoot">Absolute in-browser path WebAssetPreloader populated.</param>
    /// <param name="inputBackend">Host input backend.</param>
    /// <param name="runtime">Created runtime on success.</param>
    /// <param name="errorMessage">Validation error on failure.</param>
    internal static bool TryCreate(string assetRoot,
        WebInputBackend inputBackend,
        [NotNullWhen(true)] out Erbe? runtime,
        out string? errorMessage)
    {
        var resources = new GameResourceManager(assetRoot);
        try
        {
            GameInstallation.ValidateResources(resources);
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException)
        {
            runtime = null;
            errorMessage = ex.Message;
            return false;
        }

        runtime = ErbeFactory.Create(resources,
            GameInstallation.ExeName,
            inputBackend,
            language: null,
            useClassicInteractions: false,
            dksnMode: false);
        errorMessage = null;
        return true;
    }
}
