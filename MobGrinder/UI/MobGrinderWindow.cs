using System.Numerics;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using Newtonsoft.Json;

namespace MobGrinder;

public sealed class MobGrinderWindow : Window
{
    private readonly MobGrinderConfiguration configuration;
    private readonly MobGrinderController controller;
    private readonly Action saveConfiguration;
    private readonly Action openLogWindow;
    private readonly Action openSettingsWindow;
    private readonly Action<bool> setOverlayVisibility;
    private readonly Dictionary<string, string> selectorSearch = new(StringComparer.Ordinal);
    private int presetTargetIndex;
    private bool confirmTargetDelete;
    private MobGrinderPreset? presetPendingDeletion;
    private string beastmasterSearch = string.Empty;

    public MobGrinderWindow(
        MobGrinderConfiguration configuration,
        MobGrinderController controller,
        Action saveConfiguration,
        Action openLogWindow,
        Action<bool>? setOverlayVisibility = null,
        Action? openSettingsWindow = null)
        : base("MobGrinder")
    {
        this.configuration = configuration;
        this.controller = controller;
        this.saveConfiguration = saveConfiguration;
        this.openLogWindow = openLogWindow;
        this.openSettingsWindow = openSettingsWindow ?? (() => { });
        this.setOverlayVisibility = setOverlayVisibility ?? (_ => { });

        this.Size = new Vector2(760, 700);
        this.SizeCondition = ImGuiCond.FirstUseEver;
        this.SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(560, 420),
            MaximumSize = new Vector2(1200, 1000),
        };
        this.TitleBarButtons.Add(new TitleBarButton
        {
            Icon = FontAwesomeIcon.Cog,
            IconOffset = new Vector2(1, 1),
            Priority = -100,
            Click = _ => this.OpenSettings(),
            ShowTooltip = () => ImGui.SetTooltip("打开设置"),
        });
    }

    public void Open() => this.IsOpen = true;

    public override void OnClose() => this.ResetDeleteConfirmations();

    private void ResetDeleteConfirmations()
    {
        this.presetPendingDeletion = null;
        this.confirmTargetDelete = false;
    }

    public void OpenDiagnosticLog() => this.openLogWindow();
    public void OpenSettings() => this.openSettingsWindow();

    public void DrawOverlayContents()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(7f, 5f));
        this.DrawOverlayControls();
        ImGui.Separator();
        ImGui.TextColored(new Vector4(0.35f, 0.8f, 1f, 1f), "运行状态");
        ImGui.TextUnformatted($"当前地图：{this.controller.CurrentMapName}");
        ImGui.TextUnformatted($"状态：{this.controller.State.ToDisplayName()}");
        ImGui.PushTextWrapPos();
        ImGui.TextColored(new Vector4(1f, 0.8f, 0.35f, 1f), $"当前进展：{this.controller.StatusReason}");
        ImGui.PopTextWrapPos();
        ImGui.TextWrapped($"当前目标：{this.controller.CurrentTargetDescription}");
        ImGui.TextUnformatted($"已完成目标：{this.controller.CompletedTargetCount} / {this.controller.TotalTargetCount}");
        ImGui.Separator();
        ImGui.TextColored(new Vector4(0.35f, 0.8f, 1f, 1f), "本次目标");
        this.DrawTargetProgressList("MobGrinderOverlayTargetProgress");
        ImGui.PopStyleVar();
    }

    public override void Draw()
    {
        if (!ImGui.BeginTabBar("MobGrinderTabs"))
            return;

        if (ImGui.BeginTabItem("运行"))
        {
            this.ResetDeleteConfirmations();
            this.DrawControls();
            ImGui.Separator();
            this.DrawStatus();
            ImGui.Separator();
            this.DrawMobList();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("预设"))
        {
            this.DrawPresetTab();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("驯兽师"))
        {
            this.ResetDeleteConfirmations();
            this.DrawBeastmasterTab();
            ImGui.EndTabItem();
        }

        ImGui.EndTabBar();
    }

    private void DrawBeastmasterTab()
    {
        ImGui.TextWrapped("按图鉴顺序抓捕所选魔兽，自动跳过已解锁的图鉴。");
        this.DrawBeastmasterControls();
        if (this.controller.State != AutomationState.Stopped && !this.controller.IsBeastmasterActive)
            ImGui.TextDisabled("请先停止普通刷怪，再开始抓捕。");
        ImGui.Separator();
        ImGui.BeginDisabled(this.controller.State != AutomationState.Stopped);
        float threshold = this.configuration.BeastmasterCaptureHpPercent;
        if (ImGui.SliderFloat("捕获血量", ref threshold, 0f, 100f, "%.0f%%"))
        {
            this.configuration.BeastmasterCaptureHpPercent = threshold;
            this.saveConfiguration();
        }
        ImGui.TextDisabled("血量降至设定值时捕获，击败后检查图鉴。");
        ImGui.SameLine();
        ImGui.TextDisabled("(?)");
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            ImGui.BeginTooltip();
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 26f);
            ImGui.TextUnformatted("开始前请切换为驯兽师。");
            ImGui.TextUnformatted("使用碎击斩降低目标血量。达到设定血量并释放捕获后，使用碎击斩 → 碎咬斧 → 裂盾劈连招；后续技能未学会时，重新使用碎击斩。对其他接战野怪也使用此连招。");
            ImGui.TextUnformatted("目标低于 10 级，或比角色低至少 10 级时，开战先使用一次捕获；血量降至设定值后再次使用。");
            ImGui.TextUnformatted("设为 0% 时，仅对上述低等级目标在开战时使用捕获。目标等级不能高于角色当前等级（含等级同步）。");
            ImGui.TextUnformatted("列表包含普通野怪和 B 级狩猎怪，不含危命任务、任务及副本专属魔兽。狩猎怪可能需要较长时间寻找。");
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
        }
        if (ImGui.Button("全选"))
        {
            this.configuration.BeastmasterSelectedPets = BeastmasterCatalog.Entries.Select(entry => entry.Number).ToList();
            this.saveConfiguration();
        }
        ImGui.SameLine();
        if (ImGui.Button("取消全选"))
        {
            this.configuration.BeastmasterSelectedPets.Clear();
            this.saveConfiguration();
        }
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.TextDisabled($"已选 {this.configuration.BeastmasterSelectedPets.Count} / {BeastmasterCatalog.Entries.Count}");
        ImGui.TextUnformatted($"当前地图：{this.controller.CurrentMapName}");
        if (this.controller.IsBeastmasterActive)
            ImGui.TextWrapped($"当前进展：{this.controller.StatusReason}");
        if (!this.controller.BeastmasterDataReady
            && (!this.controller.IsBeastmasterTestActive || this.controller.CurrentTargetKillCount > 0))
            ImGui.TextWrapped(this.controller.BeastmasterDataStatus);
        ImGui.Separator();
        ImGui.InputTextWithHint("筛选##Beastmaster", "图鉴编号、魔兽、野怪或地图", ref this.beastmasterSearch, 128);
        if (!ImGui.BeginTable("BeastmasterCatalog", 6,
                ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollY,
                new Vector2(0, Math.Max(120, ImGui.GetContentRegionAvail().Y))))
            return;
        ImGui.TableSetupColumn("选择", ImGuiTableColumnFlags.WidthFixed, ImGui.CalcTextSize("选择").X + 16f);
        ImGui.TableSetupColumn("魔兽 / 目标野怪");
        ImGui.TableSetupColumn("地图");
        ImGui.TableSetupColumn("等级", ImGuiTableColumnFlags.WidthFixed, 60);
        ImGui.TableSetupColumn("图鉴状态", ImGuiTableColumnFlags.WidthFixed, ImGui.CalcTextSize("图鉴状态").X + 16f);
        ImGui.TableSetupColumn("刷新点");
        ImGui.TableHeadersRow();
        foreach (BeastmasterEntry entry in BeastmasterCatalog.Entries)
        {
            if (!string.IsNullOrWhiteSpace(this.beastmasterSearch)
                && !$"{entry.Number:00} {entry.Name} {entry.MobNames} {entry.MapName}"
                    .Contains(this.beastmasterSearch, StringComparison.CurrentCultureIgnoreCase))
                continue;
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            bool selected = this.configuration.BeastmasterSelectedPets.Contains(entry.Number);
            ImGui.BeginDisabled(this.controller.State != AutomationState.Stopped);
            if (ImGui.Checkbox($"##Beast{entry.Number}", ref selected))
            {
                if (selected) this.configuration.BeastmasterSelectedPets.Add(entry.Number);
                else this.configuration.BeastmasterSelectedPets.Remove(entry.Number);
                this.saveConfiguration();
            }
            ImGui.EndDisabled();
            ImGui.TableNextColumn();
            ImGui.TextUnformatted($"No.{entry.Number:00} {entry.Name}");
            ImGui.TextDisabled(entry.MobNames);
            ImGui.TableNextColumn();
            ImGui.TextWrapped(entry.MapName);
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(entry.MinLevel == entry.MaxLevel ? $"{entry.MinLevel}" : $"{entry.MinLevel}–{entry.MaxLevel}");
            ImGui.TableNextColumn();
            bool unlocked = this.controller.BeastmasterDataReady && this.controller.BeastmasterUnlockedPets.Contains(entry.Number);
            ImGui.TextColored(unlocked ? new Vector4(0.3f, 0.9f, 0.4f, 1f) : new Vector4(0.8f, 0.8f, 0.8f, 1f),
                !this.controller.BeastmasterDataReady ? "待读取" : unlocked ? "已解锁" : "未解锁");
            ImGui.TableNextColumn();
            var spawns = this.controller.SpawnData.GetByNameAndTerritory(entry.NameIds[0], entry.TerritoryId);
            int count = this.controller.SpawnData.GetWorldPointsByNameAndTerritory(entry.NameIds[0], entry.TerritoryId).Count;
            ImGui.TextUnformatted($"{count} 处");
            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                foreach (var point in spawns.DistinctBy(point => (point.Position.X, point.Position.Y)).Take(20))
                    ImGui.TextUnformatted($"X:{point.Position.X:0.0} Y:{point.Position.Y:0.0}");
                ImGui.TextUnformatted("按这些坐标寻找目标，最多显示 20 处。");
                ImGui.EndTooltip();
            }
        }
        ImGui.EndTable();
    }

    private void DrawBeastmasterControls()
    {
        bool paused = this.controller.State == AutomationState.Paused;
        ImGui.BeginDisabled(!this.controller.CanStartBeastmaster(testMode: false));
        if (ImGui.Button(paused && this.controller.IsBeastmasterActive && !this.controller.IsBeastmasterTestActive
                ? "继续抓捕" : "开始抓捕"))
            this.controller.StartBeastmaster();
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.BeginDisabled(!this.controller.CanStartBeastmaster(testMode: true));
        if (ImGui.Button(paused && this.controller.IsBeastmasterTestActive ? "继续测试抓捕" : "测试抓捕"))
            this.controller.StartBeastmaster(testMode: true);
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip("每种所选魔兽先击败一只，再检查图鉴；首次战斗前不跳过已解锁目标。");
        ImGui.SameLine();
        ImGui.BeginDisabled(!this.controller.IsBeastmasterActive || !this.controller.CanPause);
        if (ImGui.Button("暂停##Beastmaster"))
            this.controller.Pause();
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.BeginDisabled(!this.controller.IsBeastmasterActive || !this.controller.CanStop);
        if (ImGui.Button("停止##Beastmaster"))
            this.controller.Stop();
        ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGui.Button("运行日志##Beastmaster"))
            this.openLogWindow();
    }

    public void DrawSettingsPage(MobGrinderSettingsPage page)
    {
        string fingerprintBefore = this.GetConfigurationFingerprint();
        if (page == MobGrinderSettingsPage.Interface)
        {
            bool showOverlay = this.configuration.ShowOverlayWindow;
            if (ImGui.Checkbox("显示悬浮窗", ref showOverlay))
            {
                this.configuration.ShowOverlayWindow = showOverlay;
                this.setOverlayVisibility(showOverlay);
            }
            ImGui.TextWrapped("在悬浮窗查看进度，并开始、暂停或停止当前任务。");
        }
        else if (page == MobGrinderSettingsPage.Navigation)
            this.DrawNavigationSettings();
        else if (page == MobGrinderSettingsPage.Sound)
            this.DrawSoundSettings();

        string fingerprintAfter = this.GetConfigurationFingerprint();
        if (!string.Equals(fingerprintBefore, fingerprintAfter, StringComparison.Ordinal))
        {
            this.configuration.Normalize();
            this.saveConfiguration();
        }
    }

    private void DrawPresetTab()
    {
        string fingerprintBefore = this.GetConfigurationFingerprint();
        this.DrawRunModeSelector();
        ImGui.Separator();
        this.DrawNamedMobPresets();
        string fingerprintAfter = this.GetConfigurationFingerprint();
        if (!string.Equals(fingerprintBefore, fingerprintAfter, StringComparison.Ordinal))
        {
            this.configuration.Normalize();
            this.saveConfiguration();
        }
    }

    private void DrawControls()
    {
        ImGui.BeginDisabled(!this.controller.CanStart);
        string startLabel = this.controller.State == AutomationState.Paused && !this.controller.IsBeastmasterActive
            ? "继续刷怪" : "开始刷怪";
        if (ImGui.Button(startLabel))
            this.controller.Start();
        ImGui.EndDisabled();

        ImGui.SameLine();
        if (ImGui.Button("设置"))
            this.OpenSettings();

        ImGui.SameLine();
        if (ImGui.Button("运行日志"))
            this.openLogWindow();

        ImGui.SameLine();
        ImGui.BeginDisabled(this.controller.IsBeastmasterActive || !this.controller.CanPause);
        if (ImGui.Button("暂停"))
            this.controller.Pause();
        ImGui.EndDisabled();

        ImGui.SameLine();
        ImGui.BeginDisabled(this.controller.IsBeastmasterActive || !this.controller.CanStop);
        if (ImGui.Button("停止"))
            this.controller.Stop();
        ImGui.EndDisabled();

        if (ImGui.Button("刷新列表"))
            this.controller.ScanNow();

        ImGui.SameLine();
        if (ImGui.Button("记录目标坐标"))
            this.controller.CaptureSupplementData();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("将当前地图、角色坐标和选中野怪的编号写入运行日志，供补充刷新点使用。日志标记：MG_SUPPLEMENT_CAPTURE");
        if (this.controller.IsBeastmasterActive)
            ImGui.TextDisabled("请先停止抓捕，再开始普通刷怪。");
    }

    private void DrawOverlayControls()
    {
        float buttonWidth = MathF.Max(72f, (ImGui.GetContentRegionAvail().X - 14f) / 3f);
        ImGui.BeginDisabled(!this.controller.CanStartCurrent);
        if (ImGui.Button(this.controller.State == AutomationState.Paused ? "继续" : "开始", new Vector2(buttonWidth, 0)))
            this.controller.StartCurrentRun();
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.BeginDisabled(!this.controller.CanPause);
        if (ImGui.Button("暂停", new Vector2(buttonWidth, 0)))
            this.controller.Pause();
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.BeginDisabled(!this.controller.CanStop);
        if (ImGui.Button("停止", new Vector2(buttonWidth, 0)))
            this.controller.Stop();
        ImGui.EndDisabled();
    }

    private void DrawStatus()
    {
        ImGui.TextUnformatted($"状态：{this.controller.State.ToDisplayName()}");
        ImGui.TextWrapped($"当前进展：{this.controller.StatusReason}");
        ImGui.TextUnformatted($"当前地图：{this.controller.CurrentMapName}");
        ImGui.TextUnformatted($"战斗中：{(this.controller.IsInCombat ? "是" : "否")}    坐骑：{(this.controller.IsMounted ? (this.controller.IsInFlight ? "飞行中" : "已骑乘") : "无")}");
        string spawnPointStatus = this.controller.IsLoadingSpawnPoints
            ? "加载中"
            : $"{this.controller.CurrentSpawnPointNumber}/{this.controller.CurrentSpawnPointCount}";
        ImGui.TextWrapped($"当前目标：{this.controller.CurrentTargetDescription}");
        ImGui.TextUnformatted($"刷新点：{spawnPointStatus}    已击杀：{this.controller.CurrentTargetKillCount}");
        ImGui.TextUnformatted($"vnavmesh：{(this.controller.VnavmeshAvailable ? "可用" : "不可用")}    Lifestream：{(this.controller.LifestreamAvailable ? "可用" : "不可用")}");

        string target = this.controller.CurrentTarget?.Name.TextValue ?? "无";
        ImGui.TextUnformatted($"选中目标：{target}");

        string lastScan = this.controller.LastScanAt == DateTime.MinValue
            ? "尚未扫描"
            : this.controller.LastScanAt.ToLocalTime().ToString("HH:mm:ss");
        ImGui.TextUnformatted($"最近扫描：{lastScan}");
        ImGui.TextUnformatted($"已收录刷新点：{this.controller.SpawnData.Entries.Count} 处");
    }

    private void DrawRunModeSelector()
    {
        ImGui.TextUnformatted("一轮结束后");
        ImGui.SameLine();
        bool stopAfterOneCycle = this.configuration.RunMode == MobRunMode.StopAfterOneCycle;
        if (ImGui.Checkbox("停止刷怪", ref stopAfterOneCycle) && stopAfterOneCycle)
            this.configuration.RunMode = MobRunMode.StopAfterOneCycle;
        ImGui.SameLine();
        bool loop = this.configuration.RunMode == MobRunMode.Loop;
        if (ImGui.Checkbox("继续循环", ref loop) && loop)
            this.configuration.RunMode = MobRunMode.Loop;
        DrawHint("预设中所有目标均达到完成条件，即为一轮结束。仅适用于普通刷怪。");
    }

    private void DrawNavigationSettings()
    {
        float flightHeight = this.configuration.FlightHeight;
        SetSettingWidth("刷新点飞行高度");
        if (ImGui.SliderFloat("刷新点飞行高度", ref flightHeight, 8f, 40f, "离地 %.0f 码"))
            this.configuration.FlightHeight = flightHeight;
        ImGui.TextWrapped("巡回刷新点时的离地高度，默认 15 码。");
        ImGui.Spacing();

        float arrivalRadius = this.configuration.SpawnPointArrivalRadius;
        SetSettingWidth("刷新点停留距离");
        if (ImGui.SliderFloat("刷新点停留距离", ref arrivalRadius, 2f, 20f, "%.0f 码"))
            this.configuration.SpawnPointArrivalRadius = arrivalRadius;
        ImGui.TextWrapped("进入刷新点的此范围后，停留并寻找野怪。");
        ImGui.Spacing();

        float waitSeconds = this.configuration.SpawnPointWaitSeconds;
        SetSettingWidth("刷新点等待时间");
        if (ImGui.SliderFloat("刷新点等待时间", ref waitSeconds, 1f, 30f, "%.0f 秒"))
            this.configuration.SpawnPointWaitSeconds = waitSeconds;
        ImGui.TextWrapped("等待期间未发现目标时，前往下一个刷新点。");
        ImGui.Spacing();

        float combatApproachRadius = this.configuration.CombatApproachRadius;
        SetSettingWidth("接近目标距离");
        if (ImGui.SliderFloat("接近目标距离", ref combatApproachRadius, 2f, 10f, "水平 %.0f 码"))
            this.configuration.CombatApproachRadius = combatApproachRadius;
        ImGui.TextWrapped("与目标的水平距离小于此值时，落地准备战斗。");
    }

    private void DrawSoundSettings()
    {
        bool enableSoundAlerts = this.configuration.EnableSoundAlerts;
        if (ImGui.Checkbox("一轮完成时播放音效", ref enableSoundAlerts))
        {
            this.configuration.EnableSoundAlerts = enableSoundAlerts;
        }

        uint soundEffectId = this.configuration.SoundAlertCycleCompletedEffectId;
        ImGui.SetNextItemWidth(MathF.Max(120f, ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize("提示音效  试听").X - 32f));
        string soundPreview = soundEffectId == 0 ? "无音效" : $"音效 {soundEffectId}（<se.{soundEffectId}>）";
        if (ImGui.BeginCombo("提示音效", soundPreview))
        {
            for (uint candidate = 0; candidate <= 16; candidate++)
            {
                bool selected = soundEffectId == candidate;
                string label = candidate == 0 ? "无音效" : $"音效 {candidate}（<se.{candidate}>）";
                if (ImGui.Selectable(label, selected))
                {
                    soundEffectId = candidate;
                    this.configuration.SoundAlertCycleCompletedEffectId = candidate;
                }
                if (selected)
                    ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }
        ImGui.SameLine();
        if (ImGui.SmallButton("试听##cycle-complete-sound"))
            this.controller.PreviewSoundAlert(soundEffectId);
        ImGui.TextWrapped("普通刷怪每轮完成，或所选魔兽全部解锁后，播放所选音效。");
    }

    private static void SetSettingWidth(string label) =>
        ImGui.SetNextItemWidth(MathF.Max(120f, ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(label).X - 16f));

    private static void DrawHint(string text)
    {
        ImGui.PushTextWrapPos();
        ImGui.TextDisabled(text);
        ImGui.PopTextWrapPos();
    }

    private void DrawNamedMobPresets()
    {
        this.DrawPresetListSelector();
        MobGrinderPreset preset = this.configuration.GetActivePresetList();
        this.presetTargetIndex = Math.Clamp(this.presetTargetIndex, 0, Math.Max(0, preset.Targets.Count - 1));

        string presetName = preset.Name;
        ImGui.SetNextItemWidth(360f);
        if (ImGui.InputText("预设名称", ref presetName, 80))
            preset.Name = presetName;

        ImGui.Spacing();
        ImGui.TextUnformatted("目标列表");
        if (!ImGui.BeginChild("MobPresetTargets", new Vector2(0, 390), true))
        {
            ImGui.EndChild();
            return;
        }

        float listWidth = Math.Clamp(ImGui.GetContentRegionAvail().X * 0.46f, 300f, 420f);
        if (ImGui.BeginChild("MobPresetTargetListPanel", new Vector2(listWidth, 0), true))
        {
            float actionBarHeight = ImGui.GetFrameHeightWithSpacing() * 4f;
            if (ImGui.BeginChild("MobPresetTargetListScroll", new Vector2(0, -actionBarHeight), true))
            {
                ImGui.Spacing();
                for (int i = 0; i < preset.Targets.Count; i++)
                {
                    MobTargetPreset target = preset.Targets[i];
                    bool selected = i == this.presetTargetIndex;
                    string summary = this.BuildTargetSummary(target);
                    bool missingStopCondition = target.StopConditions.Count == 0;
                    if (missingStopCondition)
                    {
                        summary = $"⚠ {summary}";
                        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.45f, 0.2f, 1f));
                    }
                    if (ImGui.Selectable($"{summary}##mob-target-row-{i}", selected, ImGuiSelectableFlags.None, new Vector2(ImGui.GetContentRegionAvail().X, 42f)))
                    {
                        this.presetTargetIndex = i;
                        this.confirmTargetDelete = false;
                    }
                    if (missingStopCondition)
                        ImGui.PopStyleColor();
                }

                if (preset.Targets.Count == 0)
                    ImGui.TextDisabled("暂无目标，请点击“添加野怪”。");
                ImGui.EndChild();
            }

            ImGui.Separator();
            if (ImGui.Button("添加野怪"))
            {
                preset.Targets.Add(new MobTargetPreset());
                this.presetTargetIndex = preset.Targets.Count - 1;
                this.confirmTargetDelete = false;
            }
            ImGui.SameLine();
            ImGui.BeginDisabled(preset.Targets.Count == 0);
            if (ImGui.Button("复制"))
            {
                preset.Targets.Add(CloneTarget(preset.Targets[this.presetTargetIndex]));
                this.presetTargetIndex = preset.Targets.Count - 1;
                this.confirmTargetDelete = false;
            }
            ImGui.EndDisabled();
            ImGui.NewLine();
            ImGui.BeginDisabled(preset.Targets.Count == 0 || this.presetTargetIndex <= 0);
            if (ImGui.Button("上移"))
            {
                int index = this.presetTargetIndex;
                (preset.Targets[index - 1], preset.Targets[index]) = (preset.Targets[index], preset.Targets[index - 1]);
                this.presetTargetIndex--;
            }
            ImGui.EndDisabled();
            ImGui.SameLine();
            ImGui.BeginDisabled(preset.Targets.Count == 0 || this.presetTargetIndex >= preset.Targets.Count - 1);
            if (ImGui.Button("下移"))
            {
                int index = this.presetTargetIndex;
                (preset.Targets[index], preset.Targets[index + 1]) = (preset.Targets[index + 1], preset.Targets[index]);
                this.presetTargetIndex++;
            }
            ImGui.EndDisabled();
            ImGui.SameLine();
            ImGui.BeginDisabled(preset.Targets.Count == 0);
            if (ImGui.Button(this.confirmTargetDelete ? "确认删除###DeleteTarget" : "删除野怪###DeleteTarget"))
            {
                if (!this.confirmTargetDelete)
                {
                    this.confirmTargetDelete = true;
                    this.presetPendingDeletion = null;
                }
                else
                {
                    preset.Targets.RemoveAt(this.presetTargetIndex);
                    this.presetTargetIndex = Math.Clamp(this.presetTargetIndex - 1, 0, Math.Max(0, preset.Targets.Count - 1));
                    this.confirmTargetDelete = false;
                }
            }
            ImGui.EndDisabled();
            if (this.confirmTargetDelete && ImGui.SmallButton("取消删除##Target"))
                this.confirmTargetDelete = false;
            ImGui.EndChild();
        }

        ImGui.SameLine();
        if (ImGui.BeginChild("MobPresetTargetDetails", new Vector2(0, 0), true))
        {
            if (preset.Targets.Count == 0)
            {
                DrawHint("请先添加野怪，再设置目标和完成条件。");
            }
            else
            {
                this.presetTargetIndex = Math.Clamp(this.presetTargetIndex, 0, preset.Targets.Count - 1);
                this.DrawMobTargetDetails(preset.Targets[this.presetTargetIndex], this.presetTargetIndex);
            }
        }
        ImGui.EndChild();
        ImGui.EndChild();
    }

    private void DrawPresetListSelector()
    {
        this.configuration.Normalize();
        if (!ReferenceEquals(this.presetPendingDeletion, this.configuration.GetActivePresetList())
            || this.controller.State != AutomationState.Stopped)
            this.presetPendingDeletion = null;
        string currentName = this.configuration.GetActivePresetList().Name;
        if (ImGui.BeginCombo("当前预设", string.IsNullOrWhiteSpace(currentName)
                ? $"预设 {this.configuration.ActivePresetListIndex + 1}"
                : currentName))
        {
            for (int i = 0; i < this.configuration.PresetLists.Count; i++)
            {
                MobGrinderPreset preset = this.configuration.PresetLists[i];
                bool selected = i == this.configuration.ActivePresetListIndex;
                string label = string.IsNullOrWhiteSpace(preset.Name) ? $"预设 {i + 1}" : preset.Name;
                if (ImGui.Selectable($"{label}##mob-preset-{i}", selected))
                {
                    this.configuration.ActivePresetListIndex = i;
                    this.presetTargetIndex = 0;
                    this.confirmTargetDelete = false;
                    this.presetPendingDeletion = null;
                }
                if (selected)
                    ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }

        ImGui.SameLine();
        if (ImGui.Button("新建预设"))
        {
            this.configuration.PresetLists.Add(new MobGrinderPreset
            {
                Name = $"预设 {this.configuration.PresetLists.Count + 1}",
            });
            this.configuration.ActivePresetListIndex = this.configuration.PresetLists.Count - 1;
            this.presetTargetIndex = 0;
            this.confirmTargetDelete = false;
            this.presetPendingDeletion = null;
        }

        ImGui.SameLine();
        ImGui.BeginDisabled(this.configuration.PresetLists.Count <= 1 || this.controller.State != AutomationState.Stopped);
        if (ImGui.Button(this.presetPendingDeletion is null ? "删除预设###DeletePreset" : "确认删除###DeletePreset"))
        {
            if (this.presetPendingDeletion is null)
            {
                this.presetPendingDeletion = this.configuration.GetActivePresetList();
                this.confirmTargetDelete = false;
            }
            else
            {
                this.configuration.PresetLists.Remove(this.presetPendingDeletion);
                this.configuration.ActivePresetListIndex = Math.Clamp(
                    this.configuration.ActivePresetListIndex, 0, this.configuration.PresetLists.Count - 1);
                this.presetTargetIndex = 0;
                this.confirmTargetDelete = false;
                this.presetPendingDeletion = null;
            }
        }
        ImGui.EndDisabled();
        if (this.presetPendingDeletion is { } pending)
        {
            ImGui.TextWrapped($"将删除预设「{pending.Name}」及其中的全部目标。再次点击“确认删除”即可删除。");
            if (ImGui.SmallButton("取消删除##Preset"))
                this.presetPendingDeletion = null;
        }
    }

    private void DrawMobTargetDetails(MobTargetPreset target, int targetIndex)
    {
        MobSelectionEntry? selected = this.controller.SpawnData.MobTargets.FirstOrDefault(entry =>
            entry.BNpcNameId == target.BNpcNameId && entry.TerritoryTypeId == target.TerritoryTypeId);
        this.DrawMobSelector(target, targetIndex, selected);

        ImGui.Spacing();
        ImGui.TextUnformatted("完成条件");

        bool mobCountEnabled = target.StopConditions.Any(condition => condition.Kind == MobStopConditionKind.MobCount);
        if (ImGui.Checkbox("按击杀数量", ref mobCountEnabled))
        {
            target.StopConditions.RemoveAll(condition => condition.Kind == MobStopConditionKind.MobCount);
            if (mobCountEnabled)
                target.StopConditions.Add(new MobStopCondition { Kind = MobStopConditionKind.MobCount, MobCount = 1 });
        }
        if (mobCountEnabled)
        {
            MobStopCondition condition = target.StopConditions.First(item => item.Kind == MobStopConditionKind.MobCount);
            int count = condition.MobCount;
            ImGui.SetNextItemWidth(140f);
            if (ImGui.InputInt("击杀数量", ref count))
                condition.MobCount = Math.Max(1, count);
        }

        bool itemCountEnabled = target.StopConditions.Any(condition => condition.Kind == MobStopConditionKind.ItemCount);
        if (ImGui.Checkbox("按背包物品数量", ref itemCountEnabled))
        {
            if (!itemCountEnabled)
                target.StopConditions.RemoveAll(condition => condition.Kind == MobStopConditionKind.ItemCount);
            else if (!target.StopConditions.Any(condition => condition.Kind == MobStopConditionKind.ItemCount))
                target.StopConditions.Add(new MobStopCondition { Kind = MobStopConditionKind.ItemCount });
        }
        if (itemCountEnabled)
        {
            int removeIndex = -1;
            int row = 0;
            foreach (MobStopCondition condition in target.StopConditions
                         .Where(item => item.Kind == MobStopConditionKind.ItemCount)
                         .ToList())
            {
                ImGui.PushID($"mob-item-condition-{targetIndex}-{row}");
                uint itemId = condition.ItemId;
                int count = condition.ItemCount;
                this.DrawItemSelector("物品", $"mob-item-selector-{targetIndex}-{row}", ref itemId);
                ImGui.SameLine();
                ImGui.SetNextItemWidth(110f);
                if (ImGui.InputInt("数量", ref count))
                    condition.ItemCount = Math.Max(1, count);
                condition.ItemId = itemId;
                ImGui.SameLine();
                ImGui.TextDisabled(condition.ItemId == 0
                    ? "当前背包：未选择物品"
                    : $"当前背包：{this.controller.GetInventoryItemCount(condition.ItemId)}");
                ImGui.SameLine();
                if (ImGui.SmallButton("删除"))
                    removeIndex = target.StopConditions.IndexOf(condition);
                ImGui.PopID();
                row++;
            }
            if (removeIndex >= 0)
                target.StopConditions.RemoveAt(removeIndex);
            if (ImGui.Button("添加物品条件"))
                target.StopConditions.Add(new MobStopCondition { Kind = MobStopConditionKind.ItemCount });
        }

        if (target.StopConditions.Count == 0)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.45f, 0.2f, 1f));
            ImGui.TextWrapped("未设置完成条件，将持续刷此目标，直到手动停止。");
            ImGui.PopStyleColor();
        }

        ImGui.Spacing();
        DrawHint("所有勾选条件均达到要求后，完成此目标并继续其他目标。");
    }

    private void DrawMobSelector(MobTargetPreset target, int targetIndex, MobSelectionEntry? selected)
    {
        string key = $"mob-selector-{targetIndex}";
        string preview = selected?.DisplayName ?? "请选择地图和野怪";
        ImGui.SetNextItemWidth(420f);
        if (!ImGui.BeginCombo("目标野怪", preview, ImGuiComboFlags.HeightLarge))
            return;

        string search = this.GetSelectorSearch(key);
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        if (ImGui.InputTextWithHint($"##search-{key}", "输入地图或野怪名称", ref search, 128))
            this.selectorSearch[key] = search;
        ImGui.Separator();
        if (ImGui.BeginChild($"results-{key}", new Vector2(0, 280f), true))
        {
            if (string.IsNullOrWhiteSpace(search))
            {
                ImGui.TextDisabled("输入地图名或野怪名开始搜索。");
            }
            else
            {
                int shown = 0;
                foreach (MobSelectionEntry entry in this.controller.SpawnData.MobTargets.Where(item =>
                             Matches(item.DisplayName, item.BNpcNameId, search)
                             || Matches(item.MapName, item.TerritoryTypeId, search)
                             || Matches(item.MobName, item.BNpcNameId, search)))
                {
                    bool isSelected = selected?.BNpcNameId == entry.BNpcNameId
                                      && selected.TerritoryTypeId == entry.TerritoryTypeId;
                    if (ImGui.Selectable(
                            $"{entry.DisplayName}##mob-choice-{entry.TerritoryTypeId}-{entry.BNpcNameId}-{targetIndex}",
                            isSelected))
                    {
                        target.BNpcNameId = entry.BNpcNameId;
                        target.TerritoryTypeId = entry.TerritoryTypeId;
                        this.selectorSearch[key] = string.Empty;
                        ImGui.CloseCurrentPopup();
                    }
                    ImGui.SameLine();
                    ImGui.TextDisabled($"刷新点 {entry.PositionCount} 处");
                    if (++shown >= 100)
                    {
                        ImGui.TextDisabled("已显示前 100 条结果，请输入更完整的名称。");
                        break;
                    }
                }
                if (shown == 0)
                    ImGui.TextDisabled("没有匹配的地图或野怪。");
            }
            ImGui.EndChild();
        }
        ImGui.EndCombo();
    }

    private void DrawItemSelector(string label, string key, ref uint itemId)
    {
        string preview = this.controller.SpawnData.GetItemName(itemId);
        ImGui.SetNextItemWidth(270f);
        if (!ImGui.BeginCombo($"{label}##{key}", preview, ImGuiComboFlags.HeightLarge))
            return;

        string search = this.GetSelectorSearch(key);
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        if (ImGui.InputTextWithHint($"##search-{key}", "输入物品名称", ref search, 128))
            this.selectorSearch[key] = search;
        ImGui.Separator();
        if (ImGui.BeginChild($"results-{key}", new Vector2(0, 240f), true))
        {
            bool none = itemId == 0;
            if (ImGui.Selectable($"未选择物品##none-{key}", none))
            {
                itemId = 0;
                ImGui.CloseCurrentPopup();
            }

            int shown = 0;
            foreach (NamedItem item in this.controller.SpawnData.ItemNames.Where(item =>
                         Matches(item.Name, item.Id, search)))
            {
                bool selected = item.Id == itemId;
                if (ImGui.Selectable($"{item.Name}##item-{item.Id}-{key}", selected))
                {
                    itemId = item.Id;
                    this.selectorSearch[key] = string.Empty;
                    ImGui.CloseCurrentPopup();
                }
                ImGui.SameLine();
                ImGui.TextDisabled($"编号 {item.Id}");
                if (++shown >= 100)
                {
                    ImGui.TextDisabled("已显示前 100 条结果，请输入更完整的名称。");
                    break;
                }
            }
            if (shown == 0 && !string.IsNullOrWhiteSpace(search))
                ImGui.TextDisabled("没有匹配的物品。");
            ImGui.EndChild();
        }
        ImGui.EndCombo();
    }

    private string BuildTargetSummary(MobTargetPreset target)
    {
        MobSelectionEntry? entry = this.controller.SpawnData.MobTargets.FirstOrDefault(item =>
            item.BNpcNameId == target.BNpcNameId && item.TerritoryTypeId == target.TerritoryTypeId);
        string name = entry?.DisplayName ?? (target.BNpcNameId == 0 ? "未选择野怪" : $"未知野怪（编号 {target.BNpcNameId}）");
        List<string> conditions = [];
        foreach (MobStopCondition condition in target.StopConditions)
        {
            if (condition.Kind == MobStopConditionKind.MobCount)
                conditions.Add($"击杀 {Math.Max(1, condition.MobCount)}");
            else if (condition.Kind == MobStopConditionKind.ItemCount && condition.ItemId != 0)
                conditions.Add($"{this.controller.SpawnData.GetItemName(condition.ItemId)} × {Math.Max(1, condition.ItemCount)}");
        }
        return conditions.Count == 0 ? name : $"{name}（{string.Join("，", conditions)}）";
    }

    private string GetConfigurationFingerprint() => JsonConvert.SerializeObject(new
    {
        this.configuration.ActivePresetListIndex,
        this.configuration.PresetLists,
        this.configuration.FlightHeight,
        this.configuration.SpawnPointArrivalRadius,
        this.configuration.SpawnPointWaitSeconds,
        this.configuration.CombatApproachRadius,
        this.configuration.RunMode,
        this.configuration.EnableSoundAlerts,
        this.configuration.SoundAlertCycleCompletedEffectId,
        this.configuration.ShowOverlayWindow,
    });

    private string GetSelectorSearch(string key) => this.selectorSearch.GetValueOrDefault(key, string.Empty);

    private static bool Matches(string value, uint id, string search)
        => value.Contains(search, StringComparison.CurrentCultureIgnoreCase)
           || id.ToString().Contains(search, StringComparison.OrdinalIgnoreCase);

    private static MobTargetPreset CloneTarget(MobTargetPreset source) => new()
    {
        BNpcNameId = source.BNpcNameId,
        TerritoryTypeId = source.TerritoryTypeId,
        StopConditions = source.StopConditions
            .Select(condition => new MobStopCondition
            {
                Kind = condition.Kind,
                MobCount = condition.MobCount,
                ItemId = condition.ItemId,
                ItemCount = condition.ItemCount,
            })
            .ToList(),
    };

    private void DrawMobList()
    {
        ImGui.TextUnformatted($"周边野怪（{this.controller.Mobs.Count}）");
        if (!ImGui.BeginChild("MobGrinderMobList", new Vector2(0, 0), true))
        {
            ImGui.EndChild();
            return;
        }

        if (this.controller.Mobs.Count == 0)
        {
            ImGui.TextDisabled("附近暂无可攻击的野怪。");
        }
        else
        {
            foreach (MobSnapshot mob in this.controller.Mobs)
            {
                string hp = mob.MaxHp == 0 ? "?" : $"{mob.CurrentHp}/{mob.MaxHp}";
                int knownPositions = this.controller.SpawnData
                    .GetByNameAndTerritory(mob.BNpcNameId, this.controller.TerritoryId)
                    .Count;
                ImGui.TextUnformatted(
                    $"{mob.Name}" +
                    $"{(this.controller.IsCurrentConfiguredTarget(mob) ? "  [目标]" : string.Empty)}" +
                    $"{(this.controller.WasTargetSkippedThisRun(mob) ? "  [已跳过]" : string.Empty)}" +
                    $"  距离 {mob.Distance:0.0} 码  血量 {hp}  " +
                    $"刷新点 {knownPositions} 处  {(mob.IsInCombat ? "战斗中" : "未接战")}");
            }
        }

        ImGui.EndChild();
    }

    private void DrawTargetProgressList(string childId)
    {
        bool childVisible = ImGui.BeginChild(
            childId,
            new Vector2(0, 0),
            true,
            ImGuiWindowFlags.AlwaysVerticalScrollbar);
        if (childVisible)
        {
            IReadOnlyList<MobTargetProgress> progress = this.controller.TargetProgress;
            if (progress.Count == 0)
            {
                ImGui.TextDisabled("暂无目标，请在预设中添加野怪，或在驯兽师页选择魔兽。");
            }
            else
            {
                foreach (MobTargetProgress target in progress)
                {
                    Vector4 color = target.IsCompleted
                        ? new Vector4(0.35f, 0.9f, 0.45f, 1f)
                        : target.Index == this.controller.CurrentTargetIndex
                            ? new Vector4(1f, 0.85f, 0.35f, 1f)
                            : new Vector4(0.9f, 0.9f, 0.9f, 1f);
                    ImGui.TextColored(color, $"{target.Index + 1}. {target.DisplayName}");
                    ImGui.Indent(16f);
                    ImGui.TextColored(color, $"击杀：{target.KillCount}    {target.StopConditionProgress}");
                    ImGui.Unindent(16f);
                    if (target.Index + 1 < progress.Count)
                        ImGui.Separator();
                }
            }
        }
        ImGui.EndChild();
    }
}
