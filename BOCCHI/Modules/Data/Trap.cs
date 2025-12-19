using Dalamud.Game.ClientState.Objects.Types;

namespace BOCCHI.Modules.Data;

public class Trap(IGameObject obj, string hash)
{
    public uint Identifier
    {
        get => obj.BaseId;
    }

    public Position Position
    {
        get => Position.Create(obj.Position);
    }

    public string Key
    {
        get => $"{obj.Position.X:f2}:{obj.Position.Y:f2}:{obj.Position.Z:f2}";
    }

    public string Hash
    {
        get => hash;
    }
}
