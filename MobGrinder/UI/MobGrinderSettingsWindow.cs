using System.Numerics;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace MobGrinder;

public enum MobGrinderSettingsPage { Interface, Navigation, Sound }

/// <summary>Independent settings window with the same category layout as AutoFatre.</summary>
public sealed class MobGrinderSettingsWindow : Window
{
    private static readonly Vector4 HeaderColor = new(0.85f, 0.72f, 0.35f, 1f);
    private readonly Action<MobGrinderSettingsPage> drawPage;
    private MobGrinderSettingsPage selectedPage;

    public MobGrinderSettingsWindow(Action<MobGrinderSettingsPage> drawPage)
        : base("MobGrinder 设置###MobGrinderSettingsWindow")
    {
        this.drawPage = drawPage;
        this.Size = new Vector2(980, 600);
        this.SizeCondition = ImGuiCond.FirstUseEver;
        this.SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(760, 420),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    public void Open()
    {
        this.IsOpen = true;
        this.BringToFront();
    }

    public override void Draw()
    {
        float scale = ImGuiHelpers.GlobalScale;
        using var rounding = ImRaii.PushStyle(ImGuiStyleVar.FrameRounding, 4f * scale);
        using var padding = ImRaii.PushStyle(ImGuiStyleVar.FramePadding, new Vector2(8f, 4f) * scale);
        using var spacing = ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, new Vector2(8f, 6f) * scale);
        float sidebarWidth = Math.Clamp(ImGui.GetFontSize() * 15f, 220f * scale, 380f * scale);
        using (var sidebar = ImRaii.Child("##SettingsNavigation", new Vector2(sidebarWidth, 0), true))
        {
            if (sidebar)
            {
                ImGui.TextColored(HeaderColor, "设置");
                ImGui.Spacing();
                foreach (MobGrinderSettingsPage page in Enum.GetValues<MobGrinderSettingsPage>())
                    if (ImGui.Selectable($"{GetPageLabel(page)}###SettingsPage-{page}", this.selectedPage == page))
                        this.selectedPage = page;
            }
        }

        ImGui.SameLine();
        using (var content = ImRaii.Child($"##SettingsContent-{this.selectedPage}", Vector2.Zero, true))
        {
            if (content)
            {
                ImGui.TextColored(HeaderColor, GetPageLabel(this.selectedPage));
                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();
                this.drawPage(this.selectedPage);
            }
        }
    }

    private static string GetPageLabel(MobGrinderSettingsPage page) => page switch
    {
        MobGrinderSettingsPage.Interface => "界面",
        MobGrinderSettingsPage.Navigation => "移动与导航",
        MobGrinderSettingsPage.Sound => "音效",
        _ => page.ToString(),
    };
}
