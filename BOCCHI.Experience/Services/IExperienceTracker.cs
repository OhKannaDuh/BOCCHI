namespace BOCCHI.Experience.Services;

public interface IExperienceTracker
{
    double ExperiencePerHour { get; }

    float[] GetExperienceHistory(TimeSpan sampleDuration);
}
