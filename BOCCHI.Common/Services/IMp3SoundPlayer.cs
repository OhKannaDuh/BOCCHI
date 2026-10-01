namespace BOCCHI.Common.Services;

public interface IMp3SoundPlayer
{
    string SoundsDirectory { get; }

    IReadOnlyList<string> ListSounds();

    void Play(string soundName);

    void OpenSoundsFolder();
}
