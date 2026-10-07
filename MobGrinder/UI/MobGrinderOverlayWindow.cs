using System.Numerics;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;

namespace MobGrinder;

/// <summary>
/// Compact run window modelled after AutoFatre's overlay: the main configuration stays out of
/// the way while the player can still control the run and watch target progress.
/// </summary>
public sealed class MobGrinderOverlayWindow : Window
{
    private readonly IDalamudPluginInterface pluginInterface;
    private readonly MobGrinderConfiguration configuration;
    private readonly MobGrinderWindow mainWindow;

    public MobGrinderOverlayWindow(
        IDalamudPluginInterface pluginInterface,
        MobGrinderConfiguration configuration,
        MobGrinderWindow mainWindow)
        : base("MobGrinder - 悬浮窗")
    {
        this.pluginInterface = pluginInterface;
        this.configuration = configuration;
        this.mainWindow = mainWindow;

        this.Size = new Vector2(380, 520);
        this.SizeCondition = ImGuiCond.FirstUseEver;
        this.SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(300, 260),
            MaximumSize = new Vector2(700, 1000),
        };
        this.ShowCloseButton = true;
        this.RespectCloseHotkey = false;
        this.IsOpen = configuration.ShowOverlayWindow;

        this.TitleBarButtons.Add(new TitleBarButton
        {
            Icon = FontAwesomeIcon.Cog,
            IconOffset = new Vector2(1, 1),
            Priority = -100,
            Click = _ => this.mainWindow.OpenSettings(),
            ShowTooltip = () => ImGui.SetTooltip("打开设置"),
        });
        this.TitleBarButtons.Add(new TitleBarButton
        {
            Icon = FontAwesomeIcon.Home,
            IconOffset = new Vector2(1, 1),
            Priority = -100,
            Click = _ => this.mainWindow.Open(),
            ShowTooltip = () => ImGui.SetTooltip("打开主窗口"),
        });
        this.TitleBarButtons.Add(new TitleBarButton
        {
            Icon = FontAwesomeIcon.ClipboardList,
            IconOffset = new Vector2(1, 1),
            Priority = -100,
            Click = _ => this.mainWindow.OpenDiagnosticLog(),
            ShowTooltip = () => ImGui.SetTooltip("打开运行日志"),
        });
    }

    public void SetVisible(bool visible)
    {
        this.configuration.ShowOverlayWindow = visible;
        this.IsOpen = visible;
    }

    public override void Draw()
    {
        if (!this.configuration.ShowOverlayWindow)
        {
            this.IsOpen = false;
            return;
        }

        this.mainWindow.DrawOverlayContents();
    }

    public override void OnClose()
    {
        this.configuration.ShowOverlayWindow = false;
        this.pluginInterface.SavePluginConfig(this.configuration);
    }
}
