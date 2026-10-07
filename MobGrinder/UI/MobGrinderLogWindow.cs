using System.Numerics;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace MobGrinder;

/// <summary>
/// Separate runtime diagnostic window, matching AutoFatre's bounded in-memory log behavior.
/// </summary>
public sealed class MobGrinderLogWindow : Window
{
    private readonly MobGrinderController controller;
    private bool autoScroll = true;
    private bool jumpToBottom;

    public MobGrinderLogWindow(MobGrinderController controller)
        : base("MobGrinder - 运行日志")
    {
        this.controller = controller;
        this.Size = new Vector2(900, 620);
        this.SizeCondition = ImGuiCond.FirstUseEver;
        this.SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(560, 320),
            MaximumSize = new Vector2(1600, 1200),
        };
    }

    public void Open() => this.IsOpen = true;

    public override void Draw()
    {
        ImGui.TextDisabled("保留最近 500 条日志。向上翻阅时暂停自动滚动。");
        if (ImGui.Button("复制全部日志"))
            ImGui.SetClipboardText(this.BuildLogText());
        ImGui.SameLine();
        if (ImGui.Button("跳到底部"))
        {
            this.autoScroll = true;
            this.jumpToBottom = true;
        }
        ImGui.SameLine();
        ImGui.Checkbox("自动滚动", ref this.autoScroll);
        ImGui.SameLine();
        if (ImGui.Button("清空日志"))
            this.controller.ClearDiagnostics();
        ImGui.Separator();

        bool childVisible = ImGui.BeginChild(
            "MobGrinderDiagnosticLogBody",
            new Vector2(0, 0),
            true,
            ImGuiWindowFlags.AlwaysVerticalScrollbar);
        if (childVisible)
        {
            bool wasAtBottom = ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - 4f;
            IReadOnlyList<DiagnosticEntry> entries = this.controller.Diagnostics;
            if (entries.Count == 0)
            {
                ImGui.TextDisabled("暂无运行日志。");
            }
            else
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    DiagnosticEntry entry = entries[i];
                    ImGui.PushStyleColor(ImGuiCol.Text, DiagnosticColor(entry.Severity));
                    ImGui.TextWrapped($"{entry.Timestamp:HH:mm:ss.fff} [{DiagnosticSeverityLabel(entry.Severity)}] {entry.Message}");
                    ImGui.PopStyleColor();
                    if (i + 1 < entries.Count)
                        ImGui.Separator();
                }
            }

            if (this.autoScroll
                && (wasAtBottom || ImGui.IsWindowAppearing() || this.jumpToBottom))
                ImGui.SetScrollHereY(1f);
            this.jumpToBottom = false;
        }
        ImGui.EndChild();
    }

    private string BuildLogText() => string.Join(
        Environment.NewLine,
        this.controller.Diagnostics.Select(entry =>
            $"{entry.Timestamp:HH:mm:ss.fff} [{DiagnosticSeverityLabel(entry.Severity)}] {entry.Message}"));

    private static Vector4 DiagnosticColor(DiagnosticSeverity severity) => severity switch
    {
        DiagnosticSeverity.Error => new Vector4(1f, 0.3f, 0.3f, 1f),
        DiagnosticSeverity.Warning => new Vector4(1f, 0.75f, 0.25f, 1f),
        DiagnosticSeverity.Information => new Vector4(0.7f, 0.85f, 1f, 1f),
        _ => new Vector4(0.65f, 0.65f, 0.65f, 1f),
    };

    private static string DiagnosticSeverityLabel(DiagnosticSeverity severity) => severity switch
    {
        DiagnosticSeverity.Error => "错误",
        DiagnosticSeverity.Warning => "警告",
        DiagnosticSeverity.Information => "信息",
        _ => "调试",
    };
}
