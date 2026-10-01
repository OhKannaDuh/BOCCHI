using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using NAudio.Wave;
using Ocelot.Lifecycle;
using System.Diagnostics;

namespace BOCCHI.Common.Services;

public sealed class Mp3SoundPlayer(IDalamudPluginInterface plugin, IPluginLog log) : IMp3SoundPlayer, IOnStop, IDisposable
{
    private readonly object gate = new();
    private Mp3FileReader? reader;
    private WaveOutEvent? waveOut;

    public string SoundsDirectory
    {
        get
        {
            string dir = Path.Combine(plugin.AssemblyLocation.DirectoryName ?? ".", "Sounds");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public IReadOnlyList<string> ListSounds()
    {
        string dir = SoundsDirectory;
        if (!Directory.Exists(dir))
        {
            return [];
        }

        return Directory.GetFiles(dir, "*.mp3")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
            .Cast<string>()
            .ToArray();
    }

    public void Play(string soundName)
    {
        if (string.IsNullOrWhiteSpace(soundName))
        {
            return;
        }

        string path = Path.Combine(SoundsDirectory, $"{soundName}.mp3");
        if (!File.Exists(path))
        {
            log.Warning("Hunt complete sound not found: {0}", path);
            return;
        }

        lock (gate)
        {
            try
            {
                DisposeAudio();
                reader = new Mp3FileReader(path);
                waveOut = new WaveOutEvent();
                waveOut.PlaybackStopped += OnPlaybackStopped;
                waveOut.Init(reader);
                waveOut.Play();
            }
            catch (Exception ex)
            {
                log.Warning(ex, "Failed to play MP3 sound {0}", soundName);
                DisposeAudio();
            }
        }
    }

    public void OpenSoundsFolder()
    {
        string dir = SoundsDirectory;
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = dir,
            UseShellExecute = true,
        });
    }

    public void OnStop() => Dispose();

    public void Dispose()
    {
        lock (gate)
        {
            DisposeAudio();
        }
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        lock (gate)
        {
            DisposeAudio();
        }
    }

    private void DisposeAudio()
    {
        if (waveOut != null)
        {
            waveOut.PlaybackStopped -= OnPlaybackStopped;
            waveOut.Dispose();
            waveOut = null;
        }

        reader?.Dispose();
        reader = null;
    }
}
