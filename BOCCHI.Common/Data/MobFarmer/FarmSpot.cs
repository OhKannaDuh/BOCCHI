using System.Numerics;
using Newtonsoft.Json;

namespace BOCCHI.Common.Data.MobFarmer;

[Serializable]
public class FarmSpot
{
    public string Name { get; set; } = "Spot";

    public bool Enabled { get; set; } = true;

    public int Priority { get; set; }

    public float X { get; set; }

    public float Y { get; set; }

    public float Z { get; set; }

    public bool UseStackPoint { get; set; }

    public float StackX { get; set; }

    public float StackY { get; set; }

    public float StackZ { get; set; }

    public int MinimumMobsToStartFight { get; set; }

    [JsonIgnore]
    public Vector3 Origin => new(X, Y, Z);

    [JsonIgnore]
    public Vector3? StackPoint => UseStackPoint ? new Vector3(StackX, StackY, StackZ) : null;

    public void SetOrigin(Vector3 position)
    {
        X = position.X;
        Y = position.Y;
        Z = position.Z;
    }

    public void SetStackPoint(Vector3 position)
    {
        StackX = position.X;
        StackY = position.Y;
        StackZ = position.Z;
        UseStackPoint = true;
    }
}
