using BOCCHI.Common.Config;
using Dalamud.Plugin.Services;

namespace BOCCHI.Common;

/// <summary>Plugin chat notifications: always tagged [BOCCHI] when shown; optional silence.</summary>
public static class BocchiChat
{
    public const string Tag = "[BOCCHI]";

    public static bool ShouldPrint(UIConfig ui) => ui.ShowBocchiChatPrefix;

    public static void Print(IChatGui chat, UIConfig ui, string message)
    {
        if (!ShouldPrint(ui))
        {
            return;
        }

        chat.Print(Format(message));
    }

    public static void PrintError(IChatGui chat, UIConfig ui, string message)
    {
        if (!ShouldPrint(ui))
        {
            return;
        }

        chat.PrintError(Format(message));
    }

    private static string Format(string message) => $"{Tag} {Strip(message)}";

    private static string Strip(string message)
    {
        if (message.StartsWith($"{Tag} ", StringComparison.Ordinal))
        {
            return message[(Tag.Length + 1)..];
        }

        if (message.StartsWith(Tag, StringComparison.Ordinal))
        {
            return message[Tag.Length..].TrimStart();
        }

        return message;
    }
}
