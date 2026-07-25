using System;
using System.Runtime.InteropServices.JavaScript;
using Game.Shared.Host.Audio;

namespace Game.Web;

/// <summary>
///     Plays the game's single streamed music asset (the Amiga intro track) through an HTML5 &lt;audio&gt;
///     element, mapping the MonoGame content-pipeline asset name ("intro") to the equivalent static file served
///     from wwwroot/Game. Browsers decode mp3 natively, so no content-pipeline build step is needed on the web
///     host - the only web-specific work is the thin JS wrapper around HTMLAudioElement.
/// </summary>
public sealed partial class WebMusicPlayer : IHostMusicPlayer
{
    private bool _isPlaying;

    /// <inheritdoc />
    public void Dispose()
    {
        Stop();
    }

    /// <inheritdoc />
    public void Play(string assetName, bool repeat, float volume)
    {
        AudioPlay($"Game/{assetName}.mp3", repeat, ClampVolume(volume));
        _isPlaying = true;
    }

    /// <inheritdoc />
    public void SetPaused(bool paused)
    {
        if (!_isPlaying)
        {
            return;
        }

        AudioSetPaused(paused);
    }

    /// <inheritdoc />
    public void SetVolume(float volume)
    {
        AudioSetVolume(ClampVolume(volume));
    }

    /// <inheritdoc />
    public void Stop()
    {
        if (!_isPlaying)
        {
            return;
        }

        AudioStop();
        _isPlaying = false;
    }

    private static double ClampVolume(float volume)
    {
        return float.IsNaN(volume) ? 0d : Math.Clamp(volume, 0f, 1f);
    }

    [JSImport("audio.play", "main.js")]
    private static partial void AudioPlay(string url, bool repeat, double volume);

    [JSImport("audio.setPaused", "main.js")]
    private static partial void AudioSetPaused(bool paused);

    [JSImport("audio.setVolume", "main.js")]
    private static partial void AudioSetVolume(double volume);

    [JSImport("audio.stop", "main.js")]
    private static partial void AudioStop();
}
