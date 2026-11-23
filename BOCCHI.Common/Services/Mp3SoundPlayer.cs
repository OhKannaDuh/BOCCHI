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

    private string BuiltInDirectory => Path.Combine(plugin.AssemblyLocation.DirectoryName ?? ".", "Sounds");

    private bool copiedOldSounds;

    // Added sounds live in the settings folder: a plugin update replaces the install folder and took them with it.
    public string SoundsDirectory
    {
        get
        {
            string dir = Path.Combine(plugin.GetPluginConfigDirectory(), "Sounds");
            Directory.CreateDirectory(dir);
            CopyOldSoundsOnce(dir);
            return dir;
        }
    }

    private void CopyOldSoundsOnce(string dir)
    {
        if (copiedOldSounds)
        {
            return;
        }

        copiedOldSounds = true;
        string marker = Path.Combine(dir, ".copied-from-install");
        if (File.Exists(marker) || !Directory.Exists(BuiltInDirectory))
        {
            return;
        }

        try
        {
            foreach (string file in Directory.GetFiles(BuiltInDirectory, "*.mp3"))
            {
                string target = Path.Combine(dir, Path.GetFileName(file));
                if (!File.Exists(target))
                {
                    File.Copy(file, target);
                }
            }

            File.WriteAllText(marker, string.Empty);
        }
        catch (Exception ex)
        {
            log.Warning(ex, "Could not copy sounds into {0}", dir);
        }
    }

    private IReadOnlyList<string>? cachedSounds;

    private DateTime cachedSoundsUtc;

    // Drawn every frame while the setting is visible; the folder only changes when you drop a file in.
    public IReadOnlyList<string> ListSounds()
    {
        if (cachedSounds != null && DateTime.UtcNow - cachedSoundsUtc < TimeSpan.FromSeconds(2))
        {
            return cachedSounds;
        }

        cachedSoundsUtc = DateTime.UtcNow;
        return cachedSounds = ReadSounds();
    }

    private IReadOnlyList<string> ReadSounds()
    {
        return new[] { SoundsDirectory, BuiltInDirectory }
            .Where(Directory.Exists)
            .SelectMany(dir => Directory.GetFiles(dir, "*.mp3"))
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
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
            path = Path.Combine(BuiltInDirectory, $"{soundName}.mp3");
        }

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
            // A late stop from the previous sound must not cut off the one playing now.
            if (!ReferenceEquals(sender, waveOut))
            {
                return;
            }

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
