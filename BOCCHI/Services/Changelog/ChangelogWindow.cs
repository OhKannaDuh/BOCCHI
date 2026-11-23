using System.Numerics;
using BOCCHI.Config;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Ocelot.Config;
using Ocelot.Services.Translation;
using Ocelot.Windows;

namespace BOCCHI.Services.Changelog;

public interface IChangelogWindow : Ocelot.Windows.IWindow
{
    void ShowForCurrentVersion();
}

public sealed class ChangelogWindow : OcelotWindow, IChangelogWindow
{
    private readonly Configuration config;

    private readonly IConfigSaver saver;

    private readonly ITranslator<ChangelogWindow> translator;

    private string version = ChangelogText.CurrentPluginVersion;

    private IReadOnlyList<ChangelogLine> lines = [];

    private bool markSeenOnClose = true;

    public ChangelogWindow(
        Configuration config,
        IConfigSaver saver,
        ITranslator<ChangelogWindow> translator)
        : base("BOCCHI — What’s new###bocchi.changelog")
    {
        this.config = config;
        this.saver = saver;
        this.translator = translator;

        Size = new Vector2(520, 420);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(360, 240),
            MaximumSize = new Vector2(900, 800),
        };
    }

    public void ShowForCurrentVersion()
    {
        version = ChangelogText.CurrentPluginVersion;
        lines = ChangelogText.TryGetSectionForVersion(version, out string body)
            ? ChangelogText.ParseLines(body)
            : [];

        WindowName = translator.T(".title", ("version", version)) + "###bocchi.changelog";
        markSeenOnClose = true;
        IsOpen = true;
    }

    public override void OnClose()
    {
        if (markSeenOnClose)
        {
            MarkCurrentVersionSeen();
        }
    }

    protected override void Render()
    {
        if (lines.Count == 0)
        {
            ImGui.TextWrapped(translator.T(".empty"));
        }
        else
        {
            ImGui.BeginChild("##bocchi_changelog_body", new Vector2(0, -ImGui.GetFrameHeightWithSpacing() * 1.5f), false);
            foreach (ChangelogLine line in lines)
            {
                switch (line.Kind)
                {
                    case ChangelogLineKind.Heading:
                        ImGui.Spacing();
                        ImGui.TextUnformatted(line.Text);
                        ImGui.Separator();
                        break;
                    case ChangelogLineKind.Bullet:
                        ImGui.Bullet();
                        ImGui.SameLine();
                        ImGui.TextWrapped(line.Text);
                        break;
                    default:
                        ImGui.TextWrapped(line.Text);
                        break;
                }
            }

            ImGui.EndChild();
        }

        if (ImGui.Button(translator.T(".got_it"), new Vector2(-1, 0)))
        {
            MarkCurrentVersionSeen();
            markSeenOnClose = false;
            IsOpen = false;
        }
    }

    private void MarkCurrentVersionSeen()
    {
        string current = ChangelogText.CurrentPluginVersion;
        if (config.LastSeenPluginVersion == current)
        {
            return;
        }

        config.LastSeenPluginVersion = current;
        saver.Save();
    }
}
