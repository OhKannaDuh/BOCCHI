using System.Globalization;
using System.Net.Http;
using System.Numerics;
using System.Text.Json;

namespace BOCCHI.Treasure.Services;

internal static class CrowdsourceSyncHttp
{
    public static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(1);

    public static readonly TimeSpan CatalogRefreshInterval = TimeSpan.FromMinutes(5);

    public static string FormatCoord(float value) =>
        MathF.Round(value, 1).ToString("F1", CultureInfo.InvariantCulture);

    public static string PositionKey(ushort territory, Vector3 position) =>
        $"{territory}:{FormatCoord(position.X)}:{FormatCoord(position.Y)}:{FormatCoord(position.Z)}";

    public static string PositionKey(ushort territory, uint dataId, Vector3 position) =>
        $"{territory}:{dataId}:{FormatCoord(position.X)}:{FormatCoord(position.Y)}:{FormatCoord(position.Z)}";

    public static string PositionKey(ushort territory, int potFateId, bool isReroll, Vector3 position) =>
        $"{territory}:{potFateId}:{(isReroll ? 1 : 0)}:{FormatCoord(position.X)}:{FormatCoord(position.Y)}:{FormatCoord(position.Z)}";
}
