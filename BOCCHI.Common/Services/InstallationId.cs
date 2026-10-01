using System.Security.Cryptography;
using System.Text;
using Dalamud.Plugin;

namespace BOCCHI.Common.Services;

public static class InstallationId
{
    private const string FileName = "coffer-installation-id.txt";

    public static string GetHash(IDalamudPluginInterface plugin)
    {
        string path = Path.Combine(plugin.ConfigDirectory.FullName, FileName);
        if (!File.Exists(path))
        {
            File.WriteAllText(path, Guid.NewGuid().ToString("N"));
        }

        string id = File.ReadAllText(path).Trim();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id))).ToLowerInvariant();
    }
}
