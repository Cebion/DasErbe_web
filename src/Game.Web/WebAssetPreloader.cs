using System;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices.JavaScript;
using System.Threading.Tasks;

namespace Game.Web;

/// <summary>
///     Fetches the original game asset files over HTTP and writes them into the browser's in-memory virtual
///     filesystem (MEMFS, mounted at "/" by the WASM runtime) before the runtime boots. This lets
///     Game.Shared's existing GameResourceManager/AssetRootResolver/ResourceEntry read them through plain
///     System.IO calls, completely unmodified from the desktop host's code path - the only web-specific code is
///     getting the bytes there in the first place.
/// </summary>
internal static partial class WebAssetPreloader
{
    private const string AssetRoot = "/game-assets";
    private const string ServedBaseRelativePath = "Game/";

    /// <summary>
    ///     Downloads every file in <see cref="WebAssetManifest.RelativePaths" /> and writes it under
    ///     <see cref="AssetRoot" />, preserving relative subdirectories.
    /// </summary>
    /// <returns>The absolute in-browser asset root path to pass to AssetRootResolver.</returns>
    public static async Task<string> PreloadAsync()
    {
        using var client = new HttpClient
        {
            BaseAddress = new Uri(GetDocumentBaseUri())
        };

        Directory.CreateDirectory(AssetRoot);
        foreach (var relativePath in WebAssetManifest.RelativePaths)
        {
            var bytes = await client.GetByteArrayAsync(ServedBaseRelativePath + relativePath).ConfigureAwait(false);
            var localPath = Path.Combine(AssetRoot, relativePath);
            var localDirectory = Path.GetDirectoryName(localPath);
            if (!string.IsNullOrEmpty(localDirectory))
            {
                Directory.CreateDirectory(localDirectory);
            }

            await File.WriteAllBytesAsync(localPath, bytes).ConfigureAwait(false);
        }

        return AssetRoot;
    }

    [JSImport("env.getBaseUri", "main.js")]
    private static partial string GetDocumentBaseUri();
}
