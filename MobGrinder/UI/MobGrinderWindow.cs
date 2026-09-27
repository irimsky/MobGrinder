using System.Numerics;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Newtonsoft.Json;

namespace MobGrinder;

public sealed class MobGrinderWindow : Window
{
    private readonly MobGrinderConfiguration configuration;
    private readonly MobGrinderController controller;
    private readonly Action saveConfiguration;
    private readonly Action openLogWindow;
    private readonly Action<bool> setOverlayVisibility;
    private readonly Dictionary<string, string> selectorSearch = new(StringComparer.Ordinal);
    private int presetTargetIndex;
    private bool confirmPresetDelete;

    public MobGrinderWindow(
        MobGrinderConfiguration configuration,
        MobGrinderController controller,
        Action saveConfiguration,
        Action openLogWindow,
        Action<bool>? setOverlayVisibility = null)
        : base("MobGrinder")
    {
        this.configuration = configuration;
        this.controller = controller;
        this.saveConfiguration = saveConfiguration;
        this.openLogWindow = openLogWindow;
        this.setOverlayVisibility = setOverlayVisibility ?? (_ => { });

        this.Size = new Vector2(760, 700);
        this.SizeCondition = ImGuiCond.FirstUseEver;
        this.SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(560, 420),
            MaximumSize = new Vector2(1200, 1000),
        };
    }

    public void Open() => this.IsOpen = true;

    public void OpenDiagnosticLog() => this.openLogWindow();

    public void DrawOverlayContents()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(7f, 5f));
        this.DrawOverlayControls();
        ImGui.Separator();
        ImGui.TextColored(new Vector4(0.35f, 0.8f, 1f, 1f), "运行状态");
        ImGui.TextUnformatted($"状态：{this.controller.State}");
        ImGui.TextColored(new Vector4(1f, 0.8f, 0.35f, 1f), $"说明：{this.controller.StatusReason}");
        ImGui.TextUnformatted($"当前路线：{this.controller.CurrentTargetDescription}");
        ImGui.TextUnformatted($"刷怪进度：{this.controller.CompletedTargetCount} / {this.controller.TotalTargetCount}");
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

        if (ImGui.BeginTabItem("设置"))
        {
            this.DrawSettingsTab();
            ImGui.EndTabItem();
        }

        ImGui.EndTabBar();
    }

    private void DrawSettingsTab()
    {
        string fingerprintBefore = this.GetConfigurationFingerprint();

        bool showOverlay = this.configuration.ShowOverlayWindow;
        if (ImGui.Checkbox("显示悬浮窗", ref showOverlay))
        {
            this.configuration.ShowOverlayWindow = showOverlay;
            this.setOverlayVisibility(showOverlay);
            this.saveConfiguration();
        }
        ImGui.TextDisabled("悬浮窗显示开始、暂停、停止按钮，以及本次运行的目标进度。可拖到游戏角落长期查看。");
        ImGui.Separator();
        ImGui.TextUnformatted("扫描诊断设置");
        this.DrawScanSettings();

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
        if (ImGui.Button(this.controller.State == AutomationState.Paused ? "继续刷怪" : "开始刷怪"))
            this.controller.Start();
        ImGui.EndDisabled();

        ImGui.SameLine();
        if (ImGui.Button("运行日志"))
            this.openLogWindow();

        ImGui.SameLine();
        ImGui.BeginDisabled(!this.controller.CanPause);
        if (ImGui.Button("暂停"))
            this.controller.Pause();
        ImGui.EndDisabled();

        ImGui.SameLine();
        ImGui.BeginDisabled(!this.controller.CanStop);
        if (ImGui.Button("停止"))
            this.controller.Stop();
        ImGui.EndDisabled();

        ImGui.SameLine();
        if (ImGui.Button("立即扫描"))
            this.controller.ScanNow();

        ImGui.SameLine();
        if (ImGui.Button("记录补充数据"))
            this.controller.CaptureSupplementData();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("记录当前地图、角色坐标和当前游戏目标的 BNpcNameId；日志关键词：MG_SUPPLEMENT_CAPTURE");
    }

    private void DrawOverlayControls()
    {
        float buttonWidth = MathF.Max(72f, (ImGui.GetContentRegionAvail().X - 14f) / 3f);
        ImGui.BeginDisabled(!this.controller.CanStart);
        if (ImGui.Button(this.controller.State == AutomationState.Paused ? "继续" : "开始", new Vector2(buttonWidth, 0)))
            this.controller.Start();
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
        ImGui.TextUnformatted($"状态：{this.controller.State}");
        ImGui.TextUnformatted($"说明：{this.controller.StatusReason}");
        ImGui.TextUnformatted($"Territory：{this.controller.TerritoryId}");
        ImGui.TextUnformatted($"战斗中：{(this.controller.IsInCombat ? "是" : "否")}    坐骑：{(this.controller.IsMounted ? (this.controller.IsInFlight ? "飞行中" : "已骑乘") : "无")}");
        string spawnPointStatus = this.controller.IsLoadingSpawnPoints
            ? "加载中"
            : $"{this.controller.CurrentSpawnPointNumber}/{this.controller.CurrentSpawnPointCount}";
        ImGui.TextUnformatted($"当前项目：{this.controller.CurrentTargetDescription}    刷新点：{spawnPointStatus}    当前击杀：{this.controller.CurrentTargetKillCount}");
        ImGui.TextUnformatted($"vnavmesh：{(this.controller.VnavmeshAvailable ? "可用" : "不可用")}    Lifestream：{(this.controller.LifestreamAvailable ? "可用" : "不可用")}");

        string target = this.controller.CurrentTarget?.Name.TextValue ?? "无";
        ImGui.TextUnformatted($"当前游戏目标：{target}");

        string lastScan = this.controller.LastScanAt == DateTime.MinValue
            ? "从未"
            : this.controller.LastScanAt.ToLocalTime().ToString("HH:mm:ss");
        ImGui.TextUnformatted($"最近扫描：{lastScan}");
        ImGui.TextUnformatted($"静态位置数据：{this.controller.SpawnData.Entries.Count} 个位置");
    }

    private void DrawScanSettings()
    {
        ImGui.TextUnformatted("一轮完成后的运行模式");
        MobRunMode runMode = this.configuration.RunMode;
        ImGui.SetNextItemWidth(260f);
        if (ImGui.BeginCombo("运行模式", runMode switch
            {
                MobRunMode.StopAfterOneCycle => "一轮结束后停止",
                _ => "一轮结束后继续循环",
            }))
        {
            bool stopAfterOneCycle = runMode == MobRunMode.StopAfterOneCycle;
            if (ImGui.Selectable("一轮结束后停止", stopAfterOneCycle))
                this.configuration.RunMode = MobRunMode.StopAfterOneCycle;
            if (stopAfterOneCycle)
                ImGui.SetItemDefaultFocus();

            bool loop = runMode == MobRunMode.Loop;
            if (ImGui.Selectable("一轮结束后继续循环", loop))
                this.configuration.RunMode = MobRunMode.Loop;
            if (loop)
                ImGui.SetItemDefaultFocus();
            ImGui.EndCombo();
        }
        ImGui.TextDisabled("一轮是当前预设中的所有野怪项目各自满足停止条件。" );

        ImGui.Spacing();
        int maxTracked = this.configuration.MaxTrackedMobs;
        if (ImGui.SliderInt("显示数量", ref maxTracked, 1, 50))
        {
            this.configuration.MaxTrackedMobs = maxTracked;
            this.configuration.Normalize();
            this.saveConfiguration();
        }

        string filter = this.configuration.NameFilter;
        if (ImGui.InputTextWithHint("名称过滤", "留空表示全部敌对战斗 NPC", ref filter, 128))
        {
            this.configuration.NameFilter = filter;
            this.saveConfiguration();
        }

        float flightHeight = this.configuration.FlightHeight;
        if (ImGui.SliderFloat("刷新点飞行高度", ref flightHeight, 8f, 40f, "离地 %.0f yalms"))
        {
            this.configuration.FlightHeight = flightHeight;
            this.configuration.Normalize();
            this.saveConfiguration();
        }

        float arrivalRadius = this.configuration.SpawnPointArrivalRadius;
        if (ImGui.SliderFloat("刷新点到达半径", ref arrivalRadius, 2f, 20f, "%.0f yalms"))
        {
            this.configuration.SpawnPointArrivalRadius = arrivalRadius;
            this.configuration.Normalize();
            this.saveConfiguration();
        }

        float waitSeconds = this.configuration.SpawnPointWaitSeconds;
        if (ImGui.SliderFloat("刷新点等待时间", ref waitSeconds, 1f, 30f, "%.0f 秒"))
        {
            this.configuration.SpawnPointWaitSeconds = waitSeconds;
            this.configuration.Normalize();
            this.saveConfiguration();
        }

        float combatApproachRadius = this.configuration.CombatApproachRadius;
        if (ImGui.SliderFloat("战斗接近半径", ref combatApproachRadius, 2f, 10f, "水平 %.0f yalms"))
        {
            this.configuration.CombatApproachRadius = combatApproachRadius;
            this.configuration.Normalize();
            this.saveConfiguration();
        }

        ImGui.Separator();
        ImGui.TextUnformatted("音效提醒");
        bool enableSoundAlerts = this.configuration.EnableSoundAlerts;
        if (ImGui.Checkbox("启用一轮完成音效", ref enableSoundAlerts))
        {
            this.configuration.EnableSoundAlerts = enableSoundAlerts;
            this.saveConfiguration();
        }

        uint soundEffectId = this.configuration.SoundAlertCycleCompletedEffectId;
        ImGui.SetNextItemWidth(260f);
        string soundPreview = soundEffectId == 0 ? "无音效" : $"音效 {soundEffectId}（<se.{soundEffectId}>）";
        if (ImGui.BeginCombo("一轮完成音效", soundPreview))
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
        ImGui.TextDisabled("一轮预设完成时播放；选择“无音效”或游戏内置 <se.1> 至 <se.16>。循环模式下每轮完成都会播放。" );

        ImGui.TextDisabled("自动化会使用 vnavmesh/Lifestream；战斗技能由其他插件负责。野怪判定不使用 Hostile 状态位。");
    }

    private void DrawNamedMobPresets()
    {
        MobGrinderPreset preset = this.configuration.GetActivePresetList();
        this.DrawPresetListSelector();

        string presetName = preset.Name;
        ImGui.SetNextItemWidth(360f);
        if (ImGui.InputText("预设名称", ref presetName, 80))
            preset.Name = presetName;

        ImGui.Spacing();
        ImGui.TextUnformatted("预设中的野怪");
        if (!ImGui.BeginChild("MobPresetTargets", new Vector2(0, 390), true))
        {
            ImGui.EndChild();
            return;
        }

        float listWidth = Math.Clamp(ImGui.GetContentRegionAvail().X * 0.46f, 300f, 420f);
        if (ImGui.BeginChild("MobPresetTargetListPanel", new Vector2(listWidth, 0), true))
        {
            const float actionBarHeight = 112f;
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
                        this.confirmPresetDelete = false;
                    }
                    if (missingStopCondition)
                        ImGui.PopStyleColor();
                }

                if (preset.Targets.Count == 0)
                    ImGui.TextDisabled("还没有野怪，请点击“添加野怪”。");
                ImGui.EndChild();
            }

            ImGui.Separator();
            if (ImGui.Button("添加野怪"))
            {
                preset.Targets.Add(new MobTargetPreset());
                this.presetTargetIndex = preset.Targets.Count - 1;
                this.confirmPresetDelete = false;
            }
            ImGui.SameLine();
            ImGui.BeginDisabled(preset.Targets.Count == 0);
            if (ImGui.Button("复制"))
            {
                preset.Targets.Add(CloneTarget(preset.Targets[this.presetTargetIndex]));
                this.presetTargetIndex = preset.Targets.Count - 1;
                this.confirmPresetDelete = false;
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
            if (ImGui.Button(this.confirmPresetDelete ? "确认删除？" : "删除野怪"))
            {
                if (!this.confirmPresetDelete)
                {
                    this.confirmPresetDelete = true;
                }
                else
                {
                    preset.Targets.RemoveAt(this.presetTargetIndex);
                    this.presetTargetIndex = Math.Clamp(this.presetTargetIndex - 1, 0, Math.Max(0, preset.Targets.Count - 1));
                    this.confirmPresetDelete = false;
                }
            }
            ImGui.EndDisabled();
            ImGui.EndChild();
        }

        ImGui.SameLine();
        if (ImGui.BeginChild("MobPresetTargetDetails", new Vector2(0, 0), true))
        {
            if (preset.Targets.Count == 0)
            {
                ImGui.TextDisabled("从左侧添加野怪后，在这里选择地图、野怪和停止条件。");
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
                    this.confirmPresetDelete = false;
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
        }

        ImGui.SameLine();
        ImGui.BeginDisabled(this.configuration.PresetLists.Count <= 1);
        if (ImGui.Button("删除当前预设"))
        {
            this.configuration.PresetLists.RemoveAt(this.configuration.ActivePresetListIndex);
            this.configuration.ActivePresetListIndex = Math.Clamp(
                this.configuration.ActivePresetListIndex,
                0,
                this.configuration.PresetLists.Count - 1);
            this.presetTargetIndex = 0;
        }
        ImGui.EndDisabled();
    }

    private void DrawMobTargetDetails(MobTargetPreset target, int targetIndex)
    {
        MobSelectionEntry? selected = this.controller.SpawnData.MobTargets.FirstOrDefault(entry =>
            entry.BNpcNameId == target.BNpcNameId && entry.TerritoryTypeId == target.TerritoryTypeId);
        this.DrawMobSelector(target, targetIndex, selected);

        ImGui.Spacing();
        ImGui.TextUnformatted("停止条件（勾选的条件全部满足后停止当前预设）");

        bool mobCountEnabled = target.StopConditions.Any(condition => condition.Kind == MobStopConditionKind.MobCount);
        if (ImGui.Checkbox("满足击杀数量", ref mobCountEnabled))
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
        if (ImGui.Checkbox("满足物品数量", ref itemCountEnabled))
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
            ImGui.TextUnformatted("⚠ 尚未设置结束条件；该项目会持续刷怪，直到手动停止。");
            ImGui.PopStyleColor();
        }

        ImGui.Spacing();
        ImGui.TextDisabled("勾选的停止条件全部满足后进入预设下一个项目；没有停止条件的项目会持续刷到手动停止。");
    }

    private void DrawMobSelector(MobTargetPreset target, int targetIndex, MobSelectionEntry? selected)
    {
        string key = $"mob-selector-{targetIndex}";
        string preview = selected?.DisplayName ?? "请选择地图 | 野怪";
        ImGui.SetNextItemWidth(420f);
        if (!ImGui.BeginCombo("地图 | 野怪", preview, ImGuiComboFlags.HeightLarge))
            return;

        string search = this.GetSelectorSearch(key);
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        if (ImGui.InputTextWithHint($"##search-{key}", "搜索地图或野怪名称...", ref search, 128))
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
                    ImGui.TextDisabled($"位置 {entry.PositionCount} · 名称编号 {entry.BNpcNameId}");
                    if (++shown >= 100)
                    {
                        ImGui.TextDisabled("仅显示前 100 条结果，请继续缩小关键词范围。");
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
        if (ImGui.InputTextWithHint($"##search-{key}", "搜索物品名称...", ref search, 128))
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
                    ImGui.TextDisabled("仅显示前 100 条结果，请继续缩小关键词范围。");
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
        string name = entry?.DisplayName ?? $"未知目标（{target.TerritoryTypeId}/{target.BNpcNameId}）";
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
        this.configuration.MaxTrackedMobs,
        this.configuration.NameFilter,
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
        ImGui.TextUnformatted($"候选野怪（{this.controller.Mobs.Count}）");
        if (!ImGui.BeginChild("MobGrinderMobList", new Vector2(0, 0), true))
        {
            ImGui.EndChild();
            return;
        }

        if (this.controller.Mobs.Count == 0)
        {
            ImGui.TextDisabled("尚未发现符合条件的野怪。");
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
                    $"{mob.Name} [{mob.BNpcNameId}]" +
                    $"{(this.controller.IsCurrentConfiguredTarget(mob) ? "  [当前预设目标]" : string.Empty)}" +
                    $"{(this.controller.WasTargetSkippedThisRun(mob) ? "  [本次运行已跳过]" : string.Empty)}" +
                    $"  距离 {mob.Distance:0.0}  HP {hp}  " +
                    $"静态位置 {knownPositions}  {(mob.IsInCombat ? "战斗中" : "未接战")}");
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
                ImGui.TextDisabled("当前预设还没有目标野怪。");
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
