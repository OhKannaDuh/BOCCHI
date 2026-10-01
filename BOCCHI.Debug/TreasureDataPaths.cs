namespace BOCCHI.Debug;

public static class TreasureDataPaths
{
    public static string? FindRepoTreasureDataRoot(string? start)
    {
        string? dir = start;
        for (int i = 0; i < 8 && dir != null; i++)
        {
            string candidate = Path.Combine(dir, "BOCCHI.Treasure", "Data");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        return null;
    }

    public static List<string> WriteZoneDataFile(
        string? pluginDir,
        string zoneFolder,
        string filename,
        string contents)
    {
        List<string> written = [];

        if (!string.IsNullOrEmpty(pluginDir))
        {
            string runtimePath = Path.Combine(pluginDir, "Data", zoneFolder, filename);
            Directory.CreateDirectory(Path.GetDirectoryName(runtimePath)!);
            File.WriteAllText(runtimePath, contents);
            written.Add(runtimePath);
        }

        string? sourceRoot = FindRepoTreasureDataRoot(pluginDir);
        if (sourceRoot != null)
        {
            string sourcePath = Path.Combine(sourceRoot, zoneFolder, filename);
            Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
            File.WriteAllText(sourcePath, contents);
            written.Add(sourcePath);
        }

        if (written.Count == 0)
        {
            throw new InvalidOperationException("Could not resolve a Data folder to write into.");
        }

        return written;
    }
}
