using System.Numerics;
using System.Globalization;

using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;

namespace MobGrinder;

/// <summary>
/// Framework-thread state machine for named-mob navigation. The combat rotation remains owned by
/// another plugin; this controller only selects targets, moves, and waits for a clean aggro state.
/// </summary>
public sealed class MobGrinderController : IDisposable, IAsyncDisposable
{
    private static readonly TimeSpan ScanInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan TeleportTimeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan MountRetryDelay = TimeSpan.FromSeconds(2);
    private const int MaxDiagnostics = 500;
    private const float CombatEngagementDistance = 20f;
    private const float AggroSearchRadius = 100f;
    private const float GroundWalkDistance = 20f;
    private const float GroundMountDistance = 25f;
    private const float GroundNavigationStallSeconds = 5f;
    private const uint SprintActionId = 4;

    private enum TargetApproachMode
    {
        Walk,
        GroundMount,
        Fly,
    }

    private readonly record struct MobEligibility(
        bool IsEligible,
        string Reason,
        NativeMobState NativeState);

    private readonly MobGrinderConfiguration configuration;
    private readonly IPluginLog log;
    private readonly IFramework framework;
    private readonly IClientState clientState;
    private readonly IPlayerState playerState;
    private readonly IObjectTable objectTable;
    private readonly ITargetManager targetManager;
    private readonly ICondition condition;
    private readonly IPartyList partyList;
    private readonly MobSpawnDataService spawnData;
    private readonly VnavmeshIpc vnavmesh;
    private readonly LifestreamIpc lifestream;
    private readonly AetheryteTravelPlanner travelPlanner;
    private readonly InventoryCounter inventoryCounter;
    private readonly MountAdapter mount;
    private readonly LandingAdapter landing;
    private readonly FieldNavigation.NoFlyZoneCatalog noFlyZones;
    private readonly FieldNavigation.NavigationTravelSession travelSession;
    private readonly SoundAlertAdapter soundAlerts;
    private readonly NativeGameStateAdapter nativeGameState;
    private readonly Queue<DiagnosticEntry> diagnostics = new();
    private readonly object diagnosticsLock = new();
    private readonly CancellationTokenSource shutdown = new();
    private readonly object pendingFrameworkTasksLock = new();
    private readonly HashSet<Task> pendingFrameworkTasks = [];
    private int disposed;

    private DateTime nextScanAt = DateTime.MinValue;
    private DateTime stateStartedAt = DateTime.MinValue;
    private DateTime teleportRequestedAt = DateTime.MinValue;
    private DateTime teleportCompletedAt = DateTime.MinValue;
    private DateTime spawnWaitUntil = DateTime.MinValue;
    private DateTime targetLostAt = DateTime.MinValue;
    private DateTime combatWaitDeadline = DateTime.MinValue;
    private DateTime lastHoverResolutionDiagnosticAt = DateTime.MinValue;
    private DateTime nextTargetDetectionDiagnosticAt = DateTime.MinValue;
    private DateTime nextMountAttemptAt = DateTime.MinValue;
    private DateTime nextDismountAttemptAt = DateTime.MinValue;
    private DateTime nextNavigationSnapshotAt = DateTime.MinValue;
    private DateTime lastAggroSeenAt = DateTime.MinValue;
    private DateTime combatBecameIdleAt = DateTime.MinValue;
    private IReadOnlyList<MobSnapshot> mobs = [];
    private IReadOnlyList<Vector3> spawnPoints = [];
    private int targetIndex;
    private int spawnPointIndex;
    private int currentTargetKillCount;
    private ulong selectedTargetId;
    private bool selectedTargetWasEngaged;
    private bool targetInterruptedBeforeArrival;
    private bool forceCombatReposition;
    private bool combatRepositionAttempted;
    private TargetApproachMode targetApproachMode;
    private readonly HashSet<ulong> skippedTargetObjectIds = [];
    private bool travelSessionActive;
    private string travelContext = string.Empty;
    private Vector3 currentHoverDestination;
    private bool currentHoverResolved;
    private IReadOnlyList<Vector3> spawnPointHoverDestinations = [];
    private bool spawnPointHeightsResolved;
    private string lastHoverResolutionDiagnostic = string.Empty;
    private bool walkSprintAttempted;
    private AetheryteTravelPlan? teleportPlan;
    private AutomationState pausedFromState = AutomationState.ValidatingPlan;

    public MobGrinderController(
        MobGrinderConfiguration configuration,
        IPluginLog log,
        IFramework framework,
        IClientState clientState,
        IPlayerState playerState,
        IObjectTable objectTable,
        ITargetManager targetManager,
        ICondition condition,
        IPartyList partyList,
        MobSpawnDataService spawnData,
        VnavmeshIpc vnavmesh,
        LifestreamIpc lifestream,
        AetheryteTravelPlanner travelPlanner,
        InventoryCounter inventoryCounter,
        MountAdapter mount,
        LandingAdapter landing,
        SoundAlertAdapter soundAlerts,
        FieldNavigation.NoFlyZoneCatalog noFlyZones,
        NativeGameStateAdapter nativeGameState)
    {
        this.configuration = configuration;
        this.log = log;
        this.framework = framework;
        this.clientState = clientState;
        this.playerState = playerState;
        this.objectTable = objectTable;
        this.targetManager = targetManager;
        this.condition = condition;
        this.partyList = partyList;
        this.spawnData = spawnData;
        this.vnavmesh = vnavmesh;
        this.lifestream = lifestream;
        this.travelPlanner = travelPlanner;
        this.inventoryCounter = inventoryCounter;
        this.mount = mount;
        this.landing = landing;
        this.soundAlerts = soundAlerts;
        this.noFlyZones = noFlyZones;
        this.nativeGameState = nativeGameState;
        this.travelSession = new FieldNavigation.NavigationTravelSession(vnavmesh, landing, mount);
        this.framework.Update += this.OnFrameworkUpdate;
    }

    public AutomationState State { get; private set; } = AutomationState.Stopped;
    public bool CanStart => this.State is AutomationState.Stopped or AutomationState.Paused;
    public bool CanPause => this.State is not (AutomationState.Stopped or AutomationState.Paused);
    public bool CanStop => this.State != AutomationState.Stopped;
    public string StatusReason { get; private set; } = "尚未启动";
    public DateTime LastScanAt { get; private set; } = DateTime.MinValue;
    public IReadOnlyList<MobSnapshot> Mobs => this.mobs;
    public MobSpawnDataService SpawnData => this.spawnData;
    public IGameObject? CurrentTarget => this.targetManager.Target;
    public bool IsInCombat => this.condition[ConditionFlag.InCombat];
    public bool IsCasting => this.condition[ConditionFlag.Casting];
    public bool IsMounted => this.mount.IsMounted;
    public bool IsInFlight => this.mount.IsInFlight;
    public bool VnavmeshAvailable => this.vnavmesh.IsAvailable;
    public bool LifestreamAvailable => this.lifestream.IsAvailable;
    public uint TerritoryId => this.clientState.TerritoryType;
    public string CurrentTargetDescription => this.GetCurrentTarget()?.DisplayName ?? "无";
    public int CurrentSpawnPointNumber => this.spawnPoints.Count == 0 ? 0 : this.spawnPointIndex + 1;
    public int CurrentSpawnPointCount => this.spawnPoints.Count;
    public bool IsLoadingSpawnPoints => this.spawnPoints.Count == 0
        && this.State is AutomationState.ValidatingPlan or AutomationState.AdvancingTarget;
    public int CurrentTargetKillCount => this.currentTargetKillCount;
    public IReadOnlyList<DiagnosticEntry> Diagnostics
    {
        get
        {
            lock (this.diagnosticsLock)
                return this.diagnostics.ToArray();
        }
    }

    public int GetInventoryItemCount(uint itemId) => this.inventoryCounter.Count(itemId);

    /// <summary>在游戏框架线程试听当前选择的内置音效。</summary>
    public void PreviewSoundAlert(uint soundEffectId) => this.QueueFrameworkAction(() =>
    {
        try
        {
            soundEffectId = Math.Clamp(soundEffectId, 0u, 16u);
            if (soundEffectId == 0)
            {
                this.AddDiagnostic(DiagnosticSeverity.Information, "试听音效已设为无音效");
                return;
            }

            this.soundAlerts.Play(soundEffectId);
            this.AddDiagnostic(DiagnosticSeverity.Information, $"已试听游戏内置音效（音效 {soundEffectId}）");
        }
        catch (Exception ex)
        {
            this.log.Warning(ex, "试听游戏内置音效失败");
        }
    }, "试听音效");

    public bool IsCurrentConfiguredTarget(MobSnapshot mob)
    {
        MobTargetPreset? target = this.configuration.GetActivePresetList().Targets.ElementAtOrDefault(this.targetIndex);
        return target is not null
            && target.BNpcNameId == mob.BNpcNameId
            && target.TerritoryTypeId == this.TerritoryId;
    }

    public bool WasTargetSkippedThisRun(MobSnapshot mob) =>
        this.skippedTargetObjectIds.Contains(mob.GameObjectId);

    public void ClearDiagnostics()
    {
        lock (this.diagnosticsLock)
            this.diagnostics.Clear();
    }

    public void Start() => this.QueueFrameworkAction(this.StartOnFrameworkThread, "启动");

    private void StartOnFrameworkThread()
    {
        if (!this.CanStart)
            return;
        this.configuration.Enabled = true;
        if (this.State == AutomationState.Stopped)
        {
            this.ResetRuntime();
            this.SetState(AutomationState.ValidatingPlan, "正在验证当前预设");
            this.log.Information("MobGrinder 自动刷怪已启动");
        }
        else
        {
            this.SetState(this.pausedFromState, "已继续自动流程");
            this.log.Information("MobGrinder 自动刷怪已继续");
        }
    }

    public void Pause() => this.QueueFrameworkAction(this.PauseOnFrameworkThread, "暂停");

    private void PauseOnFrameworkThread()
    {
        if (!this.CanPause)
            return;
        this.pausedFromState = this.State;
        this.configuration.Enabled = false;
        this.StopMovement();
        this.SetState(AutomationState.Paused, "已暂停；导航已停止，未释放技能");
        this.log.Information("MobGrinder 已暂停");
    }

    public void Stop() => this.QueueFrameworkAction(this.StopOnFrameworkThread, "停止");

    private void StopOnFrameworkThread()
    {
        if (!this.CanStop)
            return;

        this.configuration.Enabled = false;
        this.StopMovement();
        this.ResetRuntime();
        this.pausedFromState = AutomationState.ValidatingPlan;
        this.SetState(AutomationState.Stopped, "已停止");
        this.mobs = [];
        this.log.Information("MobGrinder 已停止");
    }

    public void ScanNow() => this.QueueFrameworkAction(this.ScanNowOnFrameworkThread, "立即扫描");

    private void ScanNowOnFrameworkThread()
    {
        this.nextScanAt = DateTime.MinValue;
        AutomationState previousState = this.State;
        this.Scan(DateTime.UtcNow);
        if (previousState is AutomationState.Stopped or AutomationState.Paused)
            this.StatusReason = previousState == AutomationState.Stopped
                ? "已执行一次手动扫描；自动流程仍未启动"
                : "已执行一次手动扫描；仍处于暂停状态";
    }

    public void CaptureSupplementData() => this.QueueFrameworkAction(this.CaptureSupplementDataOnFrameworkThread, "采集补充数据");

    private void CaptureSupplementDataOnFrameworkThread()
    {
        IPlayerCharacter? player = this.objectTable.LocalPlayer;
        if (!this.clientState.IsLoggedIn || !this.playerState.IsLoaded || player is null)
        {
            this.AddDiagnostic(
                DiagnosticSeverity.Warning,
                "[MG_SUPPLEMENT_CAPTURE] 采集失败：角色尚未登录或对象表未就绪");
            return;
        }

        IBattleNpc? target = this.targetManager.Target as IBattleNpc;
        MobTargetPreset? configuredTarget = this.configuration.GetActivePresetList().Targets.ElementAtOrDefault(this.targetIndex);
        string position = string.Create(
            CultureInfo.InvariantCulture,
            $"{player.Position.X:0.######};{player.Position.Y:0.######};{player.Position.Z:0.######}");
        string targetName = target?.Name.TextValue ?? "无";
        uint configuredNameId = configuredTarget?.BNpcNameId ?? 0;

        this.AddDiagnostic(
            DiagnosticSeverity.Information,
            $"[MG_SUPPLEMENT_CAPTURE] TerritoryTypeId={this.TerritoryId}; "
            + $"BNpcNameId={(target?.NameId ?? 0)}; Position={position}; "
            + $"GameTargetObjectId=0x{(target?.GameObjectId ?? 0):X}; GameTargetName=\"{targetName}\"; "
            + $"ConfiguredBNpcNameId={configuredNameId}");
    }

    private void QueueFrameworkAction(Action action, string operation)
    {
        if (Volatile.Read(ref this.disposed) != 0)
            return;

        Task task;
        try
        {
            task = this.framework.RunOnTick(() =>
            {
                if (this.shutdown.IsCancellationRequested)
                    return;

                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    this.log.Error(ex, "MobGrinder Framework 操作失败：{Operation}", operation);
                }
            }, cancellationToken: this.shutdown.Token);
        }
        catch (Exception ex)
        {
            this.log.Warning(ex, "MobGrinder 无法排队 Framework 操作：{Operation}", operation);
            return;
        }

        lock (this.pendingFrameworkTasksLock)
            this.pendingFrameworkTasks.Add(task);

        _ = task.ContinueWith(
            completed =>
            {
                if (completed.IsFaulted && completed.Exception is { } exception)
                    this.log.Error(exception.GetBaseException(), "MobGrinder Framework 操作异常：{Operation}", operation);
                lock (this.pendingFrameworkTasksLock)
                    this.pendingFrameworkTasks.Remove(completed);
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref this.disposed, 1) != 0)
            return;

        this.shutdown.Cancel();
        this.framework.Update -= this.OnFrameworkUpdate;

        try
        {
            await this.framework.RunOnFrameworkThread(this.StopForDisposeOnFrameworkThread)
                .WaitAsync(TimeSpan.FromSeconds(2))
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            this.log.Warning(ex, "MobGrinder 卸载时未能在 Framework 线程完成导航停止");
        }

        Task[] pending;
        lock (this.pendingFrameworkTasksLock)
            pending = this.pendingFrameworkTasks.ToArray();
        try
        {
            await Task.WhenAll(pending).WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Cancellation is the expected result for actions waiting on a Framework tick.
        }
        catch (TimeoutException)
        {
            this.log.Warning("MobGrinder 卸载等待 Framework 队列超时；未再提交新的游戏操作");
        }

        this.travelSession.Dispose();
        this.landing.Dispose();
        this.mobs = [];
        this.configuration.Enabled = false;
        this.shutdown.Dispose();
    }

    public void Dispose() => this.DisposeAsync().AsTask().GetAwaiter().GetResult();

    private void StopForDisposeOnFrameworkThread()
    {
        this.configuration.Enabled = false;
        this.StopMovement();
        this.mobs = [];
    }

    private void OnFrameworkUpdate(IFramework _)
    {
        if (this.State is AutomationState.Stopped or AutomationState.Paused)
            return;
        DateTime now = DateTime.UtcNow;
        if (now >= this.nextScanAt)
            this.Scan(now);

        AutomationInterruption interruption = AutomationLifecyclePolicy.Evaluate(
            this.State,
            this.clientState.IsLoggedIn && this.objectTable.LocalPlayer is not null,
            this.playerState.IsLoaded,
            this.IsBetweenAreas(),
            this.lifestream.IsAvailable,
            this.vnavmesh.IsAvailable);
        if (interruption.ShouldStopNavigation)
        {
            this.StopMovement();
            if (interruption.ShouldAbortTravel)
                this.lifestream.Abort();
            if (interruption.ShouldFailAutomation)
                this.FailAutomation(interruption.Reason);
            else
                this.SetState(AutomationState.WaitingForPlayer, interruption.Reason);
            return;
        }

        if (this.TryInterruptCruiseForTarget(now))
            return;

        try
        {
            this.ProcessState(now);
        }
        catch (Exception ex)
        {
            this.StopMovement();
            this.SetState(AutomationState.Stopped, $"自动流程异常，已停止：{ex.Message}");
            this.configuration.Enabled = false;
            this.AddDiagnostic(DiagnosticSeverity.Error, $"自动流程异常：{ex}");
            this.log.Error(ex, "MobGrinder 自动流程发生未处理异常");
        }
    }

    private void ProcessState(DateTime now)
    {
        switch (this.State)
        {
            case AutomationState.WaitingForPlayer:
                this.SetState(AutomationState.ValidatingPlan, "角色已就绪，正在验证当前预设");
                break;
            case AutomationState.ValidatingPlan:
                this.ValidateCurrentTarget(now);
                break;
            case AutomationState.WaitingForVnavmesh:
                if (!this.vnavmesh.IsAvailable)
                {
                    this.StatusReason = "等待 vnavmesh 就绪";
                    return;
                }
                this.SetState(AutomationState.PreparingFlight, "vnavmesh 已就绪，准备前往刷新点");
                break;
            case AutomationState.Teleporting:
                this.ProcessTeleporting(now);
                break;
            case AutomationState.WaitingForTerritory:
                this.ProcessWaitingForTerritory(now);
                break;
            case AutomationState.PreparingFlight:
                this.PrepareFlight(now);
                break;
            case AutomationState.PreparingTargetApproach:
                this.PrepareTargetApproach(now);
                break;
            case AutomationState.NavigatingToSpawnPoint:
                this.NavigateToSpawnPoint(now);
                break;
            case AutomationState.WaitingAtSpawnPoint:
                this.WaitAtSpawnPoint(now);
                break;
            case AutomationState.FlyingToTarget:
                this.FlyToTarget(now);
                break;
            case AutomationState.LandingForCombat:
                this.LandForCombat(now);
                break;
            case AutomationState.DismountingForCombat:
                this.DismountForCombat(now);
                break;
            case AutomationState.WaitingForCombat:
                this.WaitForCombat(now);
                break;
            case AutomationState.CleaningAggro:
                this.CleanAggro(now);
                break;
            case AutomationState.AdvancingSpawnPoint:
                this.spawnPointIndex = (this.spawnPointIndex + 1) % Math.Max(1, this.spawnPoints.Count);
                this.currentHoverResolved = false;
                this.EndTravelSession();
                this.SetState(AutomationState.PreparingFlight, "当前目标未满足停止条件，前往下一个刷新点");
                break;
            case AutomationState.AdvancingTarget:
                this.AdvanceTarget(now);
                break;
        }
    }

    private void ValidateCurrentTarget(DateTime now)
    {
        MobGrinderPreset preset = this.configuration.GetActivePresetList();
        if (preset.Targets.Count == 0)
        {
            this.FailAutomation("当前预设没有野怪项目");
            return;
        }
        this.targetIndex = Math.Clamp(this.targetIndex, 0, preset.Targets.Count - 1);
        MobTargetPreset target = preset.Targets[this.targetIndex];
        if (target.BNpcNameId == 0 || target.TerritoryTypeId == 0)
        {
            this.FailAutomation($"预设第 {this.targetIndex + 1} 项尚未选择有效的地图和野怪");
            return;
        }
        if (this.AreStopConditionsMet(target))
        {
            this.SetState(AutomationState.AdvancingTarget, "当前野怪项目的停止条件已满足");
            return;
        }

        this.spawnPoints = this.GetCurrentSpawnPoints();
        this.spawnPointHoverDestinations = [];
        this.spawnPointHeightsResolved = false;
        if (this.spawnPoints.Count == 0)
        {
            int rawCount = this.spawnData.GetByNameAndTerritory(target.BNpcNameId, target.TerritoryTypeId).Count;
            string reason = rawCount == 0
                ? $"MobSpawn.csv 中没有当前项目的静态刷新点记录（BNpcNameId={target.BNpcNameId}，TerritoryTypeId={target.TerritoryTypeId}）"
                : $"当前项目读取到 {rawCount} 条静态刷新点记录，但启动时坐标反算成功 0 条（BNpcNameId={target.BNpcNameId}，TerritoryTypeId={target.TerritoryTypeId}）";
            this.AddDiagnostic(DiagnosticSeverity.Error, reason);
            this.FailAutomation(reason);
            return;
        }
        this.spawnPointIndex = Math.Clamp(this.spawnPointIndex, 0, this.spawnPoints.Count - 1);

        if (this.TerritoryId != target.TerritoryTypeId)
        {
            this.BeginTeleport(now, target);
            return;
        }
        this.SetState(AutomationState.WaitingForVnavmesh, "已在目标地图，等待 vnavmesh");
    }

    private void BeginTeleport(DateTime now, MobTargetPreset target)
    {
        this.StopMovement();
        if (this.teleportRequestedAt != DateTime.MinValue
            && now - this.teleportRequestedAt < TimeSpan.FromSeconds(3))
            return;
        if (!this.lifestream.IsAvailable)
        {
            this.FailAutomation("需要 Lifestream 执行地图传送，但当前不可用");
            return;
        }
        if (this.lifestream.IsBusy)
        {
            this.StatusReason = "等待 Lifestream 完成其他传送";
            return;
        }
        if (!this.travelPlanner.TryFindBest(target.TerritoryTypeId, this.spawnPoints[this.spawnPointIndex], out AetheryteTravelPlan plan))
        {
            this.FailAutomation("找不到目标地图中已解锁的以太之光");
            return;
        }
        this.teleportPlan = plan;
        if (!this.lifestream.Teleport(plan.AetheryteId, plan.SubIndex))
        {
            this.StatusReason = "传送请求未被 Lifestream 接受，准备重试";
            this.teleportRequestedAt = now.AddSeconds(-3);
            return;
        }
        this.teleportRequestedAt = now;
        this.SetState(AutomationState.WaitingForTerritory, $"正在传送到目标地图（以太之光 {plan.AetheryteId}）");
    }

    private void ProcessTeleporting(DateTime now)
    {
        if (this.TerritoryId == this.teleportPlan?.TerritoryId)
            this.SetState(AutomationState.WaitingForTerritory, "已切换到目标区域，等待传送完成");
        else if (now - this.stateStartedAt > TeleportTimeout)
            this.FailAutomation("传送超时");
    }

    private void ProcessWaitingForTerritory(DateTime now)
    {
        if (now - this.stateStartedAt > TeleportTimeout)
        {
            this.FailAutomation("等待目标地图超时");
            return;
        }
        if (this.teleportPlan is not { } plan || this.TerritoryId != plan.TerritoryId || this.IsBetweenAreas() || this.lifestream.IsBusy)
            return;
        if (this.teleportCompletedAt == DateTime.MinValue)
            this.teleportCompletedAt = now;
        if (now - this.teleportCompletedAt >= TimeSpan.FromSeconds(1))
            this.SetState(AutomationState.WaitingForVnavmesh, "传送完成，等待 vnavmesh 就绪");
    }

    private void PrepareFlight(DateTime now)
    {
        if (this.condition[ConditionFlag.InCombat])
        {
            this.SetState(AutomationState.CleaningAggro, "检测到接战，先清理仇恨");
            return;
        }
        if (!this.spawnPointHeightsResolved && !this.TryResolveAllSpawnPointHeights())
        {
            this.StatusReason = string.IsNullOrWhiteSpace(this.lastHoverResolutionDiagnostic)
                ? "正在预解析当前项目的全部刷新点地面高度"
                : this.lastHoverResolutionDiagnostic;
            return;
        }
        if (this.spawnPointHoverDestinations.Count != this.spawnPoints.Count)
        {
            this.spawnPointHeightsResolved = false;
            this.StatusReason = "刷新点预解析结果数量不一致，准备重新解析";
            return;
        }
        this.currentHoverDestination = this.spawnPointHoverDestinations[this.spawnPointIndex];
        this.currentHoverResolved = true;
        if (!this.travelSessionActive
            && (!this.IsInFlight && !this.TryEnsureMounted(now, "刷新点巡回")))
            return;
        if (!this.travelSessionActive)
            this.StartTravelSession(now, this.currentHoverDestination,
                FieldNavigation.NavigationTravelIntent.Fly,
                FieldNavigation.GroundDestinationKind.MapWaypoint,
                "刷新点巡回");
        this.SetState(AutomationState.NavigatingToSpawnPoint, $"前往刷新点 {this.CurrentSpawnPointNumber}/{this.CurrentSpawnPointCount}");
    }

    private void NavigateToSpawnPoint(DateTime now)
    {
        IPlayerCharacter? player = this.objectTable.LocalPlayer;
        if (player is null)
        {
            this.EndTravelSession();
            this.SetState(AutomationState.PreparingFlight, "等待角色状态恢复");
            return;
        }
        if (this.travelSession.Mode == FieldNavigation.NavigationTravelMode.Fly
            && !this.mount.IsMounted
            && !this.mount.IsInFlight
            && !this.mount.IsMountTransition)
        {
            this.EndTravelSession();
            this.SetState(AutomationState.PreparingFlight, "飞行坐骑状态已消失，重新准备巡回");
            return;
        }
        FieldNavigation.NavigationTravelUpdate update = this.TickTravelSession(
            now,
            this.currentHoverDestination,
            () => this.IsAtCurrentHover(player.Position));
        if (update.Outcome == FieldNavigation.NavigationTravelOutcome.Failed)
        {
            this.AddDiagnostic(DiagnosticSeverity.Warning,
                $"刷新点导航失败：stage={update.FailureStage}; rejection={update.Rejection}; reason={update.Reason}");
            this.EndTravelSession();
            this.SetState(AutomationState.AdvancingSpawnPoint, "当前刷新点导航失败，暂时跳过并继续巡回");
            return;
        }
        if (update.Outcome == FieldNavigation.NavigationTravelOutcome.Arrived)
        {
            this.EndTravelSession();
            this.spawnWaitUntil = now.AddSeconds(this.configuration.SpawnPointWaitSeconds);
            this.SetState(AutomationState.WaitingAtSpawnPoint, $"已到达刷新点上空，等待 {this.configuration.SpawnPointWaitSeconds:0.#} 秒");
        }
    }

    private void WaitAtSpawnPoint(DateTime now)
    {
        if (now >= this.spawnWaitUntil)
            this.SetState(AutomationState.AdvancingSpawnPoint, "刷新点等待结束，继续下一个刷新点");
    }

    private void BeginTarget(IBattleNpc target, DateTime now)
    {
        IPlayerCharacter? player = this.objectTable.LocalPlayer;
        if (player is null)
            return;

        MobEligibility eligibility = this.EvaluateMobEligibility(target, player.GameObjectId);
        if (!eligibility.IsEligible)
        {
            this.AddDiagnostic(
                DiagnosticSeverity.Information,
                $"目标在进入导航前已被过滤：{target.Name.TextValue}（对象 ID 0x{target.GameObjectId:X}，{eligibility.Reason}）");
            this.targetManager.Target = null;
            return;
        }

        this.targetInterruptedBeforeArrival = this.State == AutomationState.NavigatingToSpawnPoint
            && !this.IsAtCurrentHover(player.Position);
        this.EndTravelSession();
        this.StopMovement();
        this.selectedTargetId = target.GameObjectId;
        this.selectedTargetWasEngaged = false;
        this.combatWaitDeadline = DateTime.MinValue;
        this.forceCombatReposition = false;
        this.combatRepositionAttempted = false;
        this.targetApproachMode = GetTargetApproachMode(player.Position, target.Position);
        this.targetLostAt = DateTime.MinValue;
        this.targetManager.Target = target;
        this.walkSprintAttempted = false;
        this.SetState(AutomationState.PreparingTargetApproach,
            $"发现目标「{target.Name.TextValue}」，距离 {HorizontalDistance(player.Position, target.Position):0.0}，采用{TargetApproachLabel(this.targetApproachMode)}");
    }

    private void PrepareTargetApproach(DateTime now)
    {
        // Re-evaluate at the moment we are about to issue movement.  A target can
        // be detected while the player is still descending or while the previous
        // navigation is settling; using only the distance from BeginTarget can
        // incorrectly promote a now-near target to ground-mount navigation.
        if (!this.forceCombatReposition
            && this.objectTable.LocalPlayer is { } player
            && this.FindBattleNpc(this.selectedTargetId) is { } target)
        {
            MobEligibility eligibility = this.EvaluateMobEligibility(target, player.GameObjectId);
            if (!eligibility.IsEligible)
            {
                this.EndTravelSession();
                this.targetManager.Target = null;
                this.selectedTargetId = 0;
                this.SetState(AutomationState.PreparingFlight, $"目标不再符合接战条件：{eligibility.Reason}");
                return;
            }

            TargetApproachMode currentMode = GetTargetApproachMode(player.Position, target.Position);
            if (currentMode != this.targetApproachMode)
            {
                this.AddDiagnostic(
                    DiagnosticSeverity.Information,
                    $"目标接近方式重新评估：{TargetApproachLabel(this.targetApproachMode)} → "
                    + $"{TargetApproachLabel(currentMode)}，水平距离={HorizontalDistance(player.Position, target.Position):0.0}，"
                    + $"高度差={MathF.Abs(player.Position.Y - target.Position.Y):0.0}");
                this.targetApproachMode = currentMode;
            }
        }

        IBattleNpc? approachTarget = this.FindBattleNpc(this.selectedTargetId);
        if (approachTarget is null)
        {
            this.EndTravelSession();
            this.SetState(AutomationState.CleaningAggro, "目标已消失，开始清理剩余仇恨");
            return;
        }

        if (this.targetApproachMode == TargetApproachMode.Walk)
        {
            if (this.mount.IsInFlight)
            {
                this.landing.BeginDescending();
                this.StatusReason = "目标在 20 yalms 内，先落地步行接近";
                return;
            }
            if (!this.TryEnsureDismounted(now, "步行接近目标"))
                return;
        }
        else if (this.targetApproachMode == TargetApproachMode.GroundMount)
        {
            if (this.mount.IsInFlight)
            {
                this.landing.BeginDescending();
                this.StatusReason = "目标在 20–25 yalms 内，正在落地改用地面坐骑";
                return;
            }
            if (!this.TryEnsureMounted(now, "地面坐骑接近目标"))
                return;
        }
        else if (!this.TryEnsureMounted(now, "飞行接近目标"))
        {
            return;
        }

        this.landing.StopDescending();
        if (this.targetApproachMode == TargetApproachMode.Walk)
            this.TryReleaseWalkSprintOnce();
        this.StartTravelSession(
            now,
            approachTarget.Position,
            this.targetApproachMode == TargetApproachMode.Fly
                ? FieldNavigation.NavigationTravelIntent.Fly
                : FieldNavigation.NavigationTravelIntent.Ground,
            FieldNavigation.GroundDestinationKind.LiveObject,
            "目标接近");
        string activeTravelLabel = this.travelSession.Mode == FieldNavigation.NavigationTravelMode.Fly
            ? "飞行"
            : this.targetApproachMode == TargetApproachMode.Fly
                ? "地面导航"
                : TargetApproachLabel(this.targetApproachMode);
        this.SetState(AutomationState.FlyingToTarget, $"使用{activeTravelLabel}前往目标");
    }

    private void FlyToTarget(DateTime now)
    {
        IBattleNpc? target = this.FindBattleNpc(this.selectedTargetId);
        if (target is null || target.IsDead || target.CurrentHp == 0)
        {
            this.EndTravelSession();
            this.SetState(AutomationState.CleaningAggro, "目标已消失或死亡，开始清理剩余仇恨");
            return;
        }
        this.targetManager.Target = target;
        IPlayerCharacter? player = this.objectTable.LocalPlayer;
        if (player is null)
            return;

        MobEligibility eligibility = this.EvaluateMobEligibility(target, player.GameObjectId);
        if (!eligibility.IsEligible)
        {
            this.EndTravelSession();
            this.targetManager.Target = null;
            this.selectedTargetId = 0;
            this.SetState(AutomationState.PreparingFlight, $"目标在接近过程中不再符合接战条件：{eligibility.Reason}");
            return;
        }

        if (this.targetApproachMode == TargetApproachMode.GroundMount
            && (!this.mount.IsMounted || this.mount.IsMountTransition || this.mount.IsInFlight))
        {
            this.StopMovement();
            this.SetState(AutomationState.PreparingTargetApproach, "地面坐骑尚未稳定，暂停目标导航");
            return;
        }

        if (this.targetApproachMode == TargetApproachMode.Walk
            && (this.mount.IsMounted || this.mount.IsInFlight || this.mount.IsMountTransition))
        {
            this.StopMovement();
            this.SetState(AutomationState.PreparingTargetApproach, "步行接近要求实际已下坐骑，暂停目标导航");
            return;
        }

        if (this.targetApproachMode == TargetApproachMode.Fly
            && !this.mount.IsMounted
            && !this.mount.IsInFlight
            && !this.mount.IsMountTransition)
        {
            this.StopMovement();
            if (HorizontalDistance(player.Position, target.Position) <= this.configuration.CombatApproachRadius)
            {
                this.AddDiagnostic(DiagnosticSeverity.Warning, "飞行接近过程中坐骑状态已消失，但目标已在战斗接近半径内；直接等待战斗插件接管");
                this.BeginWaitingForCombat("目标已在接近范围内且角色已下坐骑，等待战斗插件接战");
            }
            else
            {
                this.targetApproachMode = TargetApproachMode.Walk;
                this.SetState(AutomationState.PreparingTargetApproach, "飞行坐骑状态已消失，改为步行接近目标");
            }
            return;
        }

        FieldNavigation.NavigationTravelUpdate update = this.TickTravelSession(
            now,
            target.Position,
            () => HorizontalDistance(player.Position, target.Position)
                <= this.configuration.CombatApproachRadius);
        if (update.Outcome == FieldNavigation.NavigationTravelOutcome.Failed)
        {
            ulong skippedId = this.selectedTargetId;
            this.skippedTargetObjectIds.Add(skippedId);
            this.selectedTargetId = 0;
            this.forceCombatReposition = false;
            this.targetInterruptedBeforeArrival = false;
            this.EndTravelSession();
            this.AddDiagnostic(
                DiagnosticSeverity.Warning,
                $"目标导航失败，暂时跳过对象 ID 0x{skippedId:X}：stage={update.FailureStage}; "
                + $"rejection={update.Rejection}; reason={update.Reason}");
            this.SetState(AutomationState.AdvancingSpawnPoint,
                $"目标导航失败，暂时跳过对象 ID 0x{skippedId:X}");
            return;
        }
        if (update.Outcome == FieldNavigation.NavigationTravelOutcome.Arrived)
        {
            this.forceCombatReposition = false;
            this.EndTravelSession();
            if (this.mount.IsMounted || this.mount.IsInFlight)
                this.SetState(AutomationState.LandingForCombat, "已接近目标，准备落地并交给战斗插件");
            else
                this.BeginWaitingForCombat("已接近目标，等待战斗插件接管");
        }
    }

    private void LandForCombat(DateTime now)
    {
        if (this.mount.IsInFlight)
        {
            this.landing.BeginDescending();
            this.StatusReason = "目标附近，正在下降";
            return;
        }
        if (this.targetApproachMode == TargetApproachMode.GroundMount && !this.mount.IsMounted)
        {
            this.AddDiagnostic(
                DiagnosticSeverity.Warning,
                "地面坐骑目标已到达接近距离但实际未骑乘；切换为步行接近，禁止直接进入错误的坐骑战斗等待状态");
            this.targetApproachMode = TargetApproachMode.Walk;
            this.SetState(AutomationState.PreparingTargetApproach, "地面坐骑未成功，改为步行接近目标");
            return;
        }
        this.landing.StopDescending();
        if (this.mount.IsMounted)
            this.SetState(AutomationState.DismountingForCombat, "已落地，准备下坐骑");
        else
            this.BeginWaitingForCombat("已落地，等待战斗插件接战");
    }

    private void DismountForCombat(DateTime now)
    {
        if (!this.TryEnsureDismounted(now, "战斗接近"))
            return;
        this.BeginWaitingForCombat("已下坐骑，等待战斗插件击杀目标");
    }

    private void WaitForCombat(DateTime now)
    {
        IBattleNpc? target = this.FindBattleNpc(this.selectedTargetId);
        IPlayerCharacter? player = this.objectTable.LocalPlayer;
        if (target is not null)
        {
            this.targetManager.Target = target;
            this.selectedTargetWasEngaged |= this.condition[ConditionFlag.InCombat]
                || target.TargetObjectId == player?.GameObjectId
                || this.nativeGameState.IsInCombat(target);
            if (this.selectedTargetWasEngaged)
                this.combatWaitDeadline = DateTime.MinValue;
            this.targetLostAt = DateTime.MinValue;
            if (target.IsDead || target.CurrentHp == 0)
            {
                this.SetState(AutomationState.CleaningAggro, "目标已死亡，等待所有接战目标清理完毕");
                return;
            }
        }
        else
        {
            if (this.targetLostAt == DateTime.MinValue)
                this.targetLostAt = now;
            if (now - this.targetLostAt >= TimeSpan.FromSeconds(2))
                this.SetState(AutomationState.CleaningAggro, "目标已离开对象表，确认是否已清场");
        }

        if (!this.selectedTargetWasEngaged
            && this.combatWaitDeadline != DateTime.MinValue
            && now >= this.combatWaitDeadline)
        {
            if (target is null || target.IsDead || target.CurrentHp == 0)
            {
                this.SetState(AutomationState.CleaningAggro, "战斗未开始且目标已消失，确认是否已清场");
                return;
            }

            if (this.combatRepositionAttempted)
            {
                this.skippedTargetObjectIds.Add(this.selectedTargetId);
                ulong skippedId = this.selectedTargetId;
                this.selectedTargetId = 0;
                this.forceCombatReposition = false;
                this.targetInterruptedBeforeArrival = false;
                this.combatWaitDeadline = DateTime.MinValue;
                this.StopMovement();
                this.SetState(AutomationState.AdvancingSpawnPoint,
                    $"重新飞行后仍未开始战斗，暂时跳过目标（对象 ID 0x{skippedId:X}）");
                return;
            }

            this.StopMovement();
            this.targetManager.Target = target;
            this.TriggerForcedFlightReposition(now, "落地后 10 秒未开始战斗，改用飞行重新导航到目标");
        }
    }

    private void CleanAggro(DateTime now)
    {
        IBattleNpc? aggro = this.FindAggroTarget();
        if (aggro is not null)
        {
            this.lastAggroSeenAt = now;
            this.combatBecameIdleAt = DateTime.MinValue;
            this.targetManager.Target = aggro;
            IPlayerCharacter? player = this.objectTable.LocalPlayer;
            if (player is not null && Vector3.Distance(player.Position, aggro.Position) > CombatEngagementDistance)
            {
                if (!this.travelSessionActive)
                    this.StartTravelSession(
                        now,
                        aggro.Position,
                        FieldNavigation.NavigationTravelIntent.Ground,
                        FieldNavigation.GroundDestinationKind.LiveObject,
                        "清理仇恨");
                FieldNavigation.NavigationTravelUpdate update = this.TickTravelSession(
                    now,
                    aggro.Position,
                    () => Vector3.Distance(player.Position, aggro.Position) <= CombatEngagementDistance);
                if (update.Outcome == FieldNavigation.NavigationTravelOutcome.Failed)
                {
                    this.AddDiagnostic(
                        DiagnosticSeverity.Warning,
                        $"清理仇恨导航失败：stage={update.FailureStage}; rejection={update.Rejection}; reason={update.Reason}");
                    this.EndTravelSession();
                }
            }
            this.StatusReason = $"清理接战目标：{aggro.Name.TextValue}";
            return;
        }
        if (this.condition[ConditionFlag.InCombat])
        {
            this.combatBecameIdleAt = DateTime.MinValue;
            this.StatusReason = "等待战斗插件清理最后的接战目标";
            return;
        }
        if (this.combatBecameIdleAt == DateTime.MinValue)
            this.combatBecameIdleAt = now;
        if (now - this.combatBecameIdleAt < TimeSpan.FromSeconds(1))
            return;

        this.currentTargetKillCount++;
        this.EndTravelSession();
        this.selectedTargetId = 0;
        this.selectedTargetWasEngaged = false;
        this.combatWaitDeadline = DateTime.MinValue;
        this.forceCombatReposition = false;
        this.landing.StopDescending();
        if (this.AreStopConditionsMet(this.CurrentTargetSpec()))
            this.SetState(AutomationState.AdvancingTarget, "当前野怪项目停止条件已满足，进入预设下一个项目");
        else if (this.targetInterruptedBeforeArrival)
        {
            this.targetInterruptedBeforeArrival = false;
            this.currentHoverResolved = false;
            this.SetState(AutomationState.PreparingFlight, "已脱战，返回被抢占的原刷新点上空");
        }
        else
            this.SetState(AutomationState.AdvancingSpawnPoint, "已脱离战斗，当前项目未完成，继续下一个刷新点");
    }

    private void AdvanceTarget(DateTime now)
    {
        MobGrinderPreset preset = this.configuration.GetActivePresetList();
        bool completedCycle = this.targetIndex + 1 >= preset.Targets.Count;
        if (completedCycle)
        {
            this.PlayCycleCompletedSound();
            if (this.configuration.RunMode != MobRunMode.StopAfterOneCycle)
                this.targetIndex = 0;
            else
            {
                this.FinishCycle();
                return;
            }

        }
        else
        {
            this.targetIndex++;
        }

        this.currentTargetKillCount = 0;
        this.targetInterruptedBeforeArrival = false;
        this.walkSprintAttempted = false;
        if (this.targetIndex >= preset.Targets.Count)
        {
            this.targetIndex = 0;
        }
        this.spawnPoints = this.GetCurrentSpawnPoints();
        this.spawnPointIndex = 0;
        this.nextMountAttemptAt = DateTime.MinValue;
        this.nextDismountAttemptAt = DateTime.MinValue;
        this.currentHoverResolved = false;
        this.spawnPointHoverDestinations = [];
        this.spawnPointHeightsResolved = false;
        this.teleportPlan = null;
        this.teleportRequestedAt = DateTime.MinValue;
        this.teleportCompletedAt = DateTime.MinValue;
        this.SetState(AutomationState.ValidatingPlan, $"切换到预设项目 {this.targetIndex + 1}/{preset.Targets.Count}");
    }

    private void PlayCycleCompletedSound()
    {
        if (!this.configuration.EnableSoundAlerts || this.configuration.SoundAlertCycleCompletedEffectId == 0)
            return;

        uint effectId = this.configuration.SoundAlertCycleCompletedEffectId;
        try
        {
            this.soundAlerts.Play(effectId);
            this.AddDiagnostic(DiagnosticSeverity.Information, $"一轮预设已完成，播放游戏内置音效（音效 {effectId}）");
        }
        catch (Exception ex)
        {
            this.log.Warning(ex, "播放一轮完成音效失败");
        }
    }

    private void FinishCycle()
    {
        this.StopMovement();
        this.configuration.Enabled = false;
        this.targetIndex = 0;
        this.ResetRuntime();
        this.mobs = [];
        this.pausedFromState = AutomationState.ValidatingPlan;
        this.SetState(AutomationState.Stopped, "一轮预设已完成，自动流程已停止");
        this.log.Information("MobGrinder 一轮预设已完成，自动流程已停止");
    }

    private bool TryEnsureMounted(DateTime now, string context)
    {
        if (this.mount.IsMountTransition)
        {
            this.StatusReason = $"{context}：等待坐骑状态稳定";
            return false;
        }
        if (this.mount.IsMounted)
        {
            this.nextMountAttemptAt = DateTime.MinValue;
            return true;
        }
        if (this.mount.HasMountedStateMismatch)
        {
            this.StatusReason = $"{context}：原生坐骑标志与游戏条件不同步，暂停导航";
            this.AddDiagnostic(
                DiagnosticSeverity.Debug,
                $"{context}：暂停重复上坐骑请求；conditionMounted={this.mount.IsConditionMounted}，nativeMounted={this.mount.IsNativeMounted}");
            return false;
        }
        if (now < this.nextMountAttemptAt)
        {
            this.StatusReason = $"{context}：等待上坐骑请求结果";
            return false;
        }
        if (!this.mount.CanAttemptMount)
        {
            this.StatusReason = $"{context}：{this.mount.MountBlockReason}，等待状态解除";
            return false;
        }

        bool accepted = this.mount.TryMount();
        this.nextMountAttemptAt = now + MountRetryDelay;
        this.StatusReason = accepted
            ? $"{context}：已请求上坐骑，等待坐骑状态稳定"
            : $"{context}：上坐骑请求被拒绝，2 秒后重试";
        this.AddDiagnostic(
            accepted ? DiagnosticSeverity.Information : DiagnosticSeverity.Warning,
            accepted
                ? $"{context}：上坐骑请求已提交（GeneralAction/9），进入 2 秒保护窗口；"
                  + $"conditionMounted={this.mount.IsConditionMounted}，nativeMounted={this.mount.IsNativeMounted}"
                : $"{context}：上坐骑请求被 ActionManager 拒绝（GeneralAction/9），进入 2 秒重试间隔；"
                  + $"conditionMounted={this.mount.IsConditionMounted}，nativeMounted={this.mount.IsNativeMounted}");
        return false;
    }

    private bool TryEnsureDismounted(DateTime now, string context)
    {
        if (!this.mount.IsMounted)
        {
            this.nextDismountAttemptAt = DateTime.MinValue;
            return true;
        }
        if (this.mount.IsMountTransition)
        {
            this.StatusReason = $"{context}：等待下坐骑状态稳定";
            return false;
        }
        if (now < this.nextDismountAttemptAt)
        {
            this.StatusReason = $"{context}：等待下坐骑请求结果";
            return false;
        }

        bool accepted = this.mount.TryDismount();
        this.nextDismountAttemptAt = now + MountRetryDelay;
        this.StatusReason = accepted
            ? $"{context}：已请求下坐骑，等待状态稳定"
            : $"{context}：下坐骑请求被拒绝，2 秒后重试";
        this.AddDiagnostic(
            accepted ? DiagnosticSeverity.Information : DiagnosticSeverity.Warning,
            accepted
                ? $"{context}：下坐骑请求已提交（GeneralAction/23），进入 2 秒保护窗口"
                : $"{context}：下坐骑请求被 ActionManager 拒绝（GeneralAction/23），进入 2 秒重试间隔");
        return false;
    }

    private unsafe void TryReleaseWalkSprintOnce()
    {
        if (this.walkSprintAttempted)
            return;
        this.walkSprintAttempted = true;

        if (this.nativeGameState.GetGeneralActionStatus(SprintActionId) == uint.MaxValue)
        {
            this.AddDiagnostic(DiagnosticSeverity.Warning, "步行目标导航开始时未释放冲刺：ActionManager.Instance() 为空");
            return;
        }

        uint status = this.nativeGameState.GetGeneralActionStatus(SprintActionId);
        if (status != 0)
        {
            this.AddDiagnostic(DiagnosticSeverity.Debug, $"步行目标导航开始时未释放冲刺：GeneralAction/4 当前状态码={status}");
            return;
        }

        bool accepted = this.nativeGameState.TryUseGeneralAction(SprintActionId);
        this.AddDiagnostic(
            accepted ? DiagnosticSeverity.Information : DiagnosticSeverity.Warning,
            accepted
                ? "步行目标导航开始：已请求释放冲刺（GeneralAction/4）"
                : "步行目标导航开始：冲刺请求被 ActionManager 拒绝（GeneralAction/4）");
    }

    private bool TryResolveAllSpawnPointHeights()
    {
        if (this.spawnPoints.Count == 0)
        {
            this.ReportHoverResolutionFailure("当前项目没有可供预解析的刷新点");
            return false;
        }
        if (!this.vnavmesh.IsAvailable)
        {
            this.ReportHoverResolutionFailure("vnavmesh.Nav.IsReady 返回 false 或 IPC 不可用");
            return false;
        }

        Vector3[] resolvedDestinations = new Vector3[this.spawnPoints.Count];
        for (int index = 0; index < this.spawnPoints.Count; index++)
        {
            if (!this.TryResolveSpawnPointHover(index, out resolvedDestinations[index]))
            {
                this.spawnPointHoverDestinations = [];
                this.spawnPointHeightsResolved = false;
                return false;
            }
        }

        this.spawnPointHoverDestinations = resolvedDestinations;
        this.spawnPointHeightsResolved = true;
        this.currentHoverDestination = resolvedDestinations[this.spawnPointIndex];
        this.currentHoverResolved = true;
        this.AddDiagnostic(
            DiagnosticSeverity.Information,
            $"当前项目全部刷新点预解析完成：{resolvedDestinations.Length} 个，已生成飞行终点");
        return true;
    }

    private bool TryResolveSpawnPointHover(int index, out Vector3 hoverDestination)
    {
        hoverDestination = default;
        Vector3 point = this.spawnPoints[index];
        Vector3 probe = new(point.X, 1024f, point.Z);
        if (this.vnavmesh.TryPointOnFloor(probe, out Vector3 resolved, out string floorFailure))
        {
            hoverDestination = new Vector3(resolved.X, resolved.Y + this.configuration.FlightHeight, resolved.Z);
            this.AddDiagnostic(
                DiagnosticSeverity.Information,
                $"刷新点 {index + 1}/{this.spawnPoints.Count} 地面高度解析成功（PointOnFloor）："
                + $"原始 X/Z=({point.X:0.00}, {point.Z:0.00})，地面=({resolved.X:0.00}, {resolved.Y:0.00}, {resolved.Z:0.00})，"
                + $"飞行终点 Y={hoverDestination.Y:0.00}");
            return true;
        }

        if (this.vnavmesh.TryNearestReachablePoint(point, out resolved, out string nearestFailure))
        {
            hoverDestination = new Vector3(resolved.X, resolved.Y + this.configuration.FlightHeight, resolved.Z);
            this.AddDiagnostic(
                DiagnosticSeverity.Warning,
                $"刷新点 {index + 1}/{this.spawnPoints.Count} 未能 PointOnFloor，已使用 NearestPointReachable："
                + $"原始=({point.X:0.00}, {point.Y:0.00}, {point.Z:0.00})，地面=({resolved.X:0.00}, {resolved.Y:0.00}, {resolved.Z:0.00})，"
                + $"PointOnFloor={floorFailure}");
            return true;
        }

        this.ReportHoverResolutionFailure(
            $"刷新点 {index + 1}/{this.spawnPoints.Count} 地面高度解析失败："
            + $"原始=({point.X:0.00}, {point.Y:0.00}, {point.Z:0.00})，"
            + $"PointOnFloor={floorFailure}，NearestPointReachable={nearestFailure}");
        return false;
    }

    private void ReportHoverResolutionFailure(string reason)
    {
        DateTime now = DateTime.UtcNow;
        if (reason == this.lastHoverResolutionDiagnostic
            && now - this.lastHoverResolutionDiagnosticAt < TimeSpan.FromSeconds(5))
            return;
        this.lastHoverResolutionDiagnostic = reason;
        this.lastHoverResolutionDiagnosticAt = now;
        this.StatusReason = reason;
        this.AddDiagnostic(DiagnosticSeverity.Warning, reason);
        this.log.Warning("MobGrinder 刷新点地面高度解析失败：{Reason}", reason);
    }

    private bool AreStopConditionsMet(MobTargetPreset target)
    {
        if (target.StopConditions.Count == 0)
            return false;
        return target.StopConditions.All(condition => condition.Kind switch
        {
            MobStopConditionKind.MobCount => this.currentTargetKillCount >= Math.Max(1, condition.MobCount),
            MobStopConditionKind.ItemCount => condition.ItemId != 0
                && this.inventoryCounter.Count(condition.ItemId) >= Math.Max(1, condition.ItemCount),
            _ => false,
        });
    }

    private IBattleNpc? TryFindTargetMob(MobTargetPreset target)
    {
        IPlayerCharacter? player = this.objectTable.LocalPlayer;
        if (player is null)
            return null;

        if (this.TerritoryId != target.TerritoryTypeId)
        {
            this.ReportTargetDetectionSkip(
                $"目标探测跳过：当前 TerritoryTypeId={this.TerritoryId}，目标 TerritoryTypeId={target.TerritoryTypeId}");
            return null;
        }

        IReadOnlyList<IBattleNpc> nearby = this.objectTable.OfType<IBattleNpc>().ToArray();
        this.LogTargetDetectionSnapshot(target, nearby, player.Position, player.GameObjectId);

        return nearby
            .Where(mob => mob.NameId == target.BNpcNameId
                          && !this.skippedTargetObjectIds.Contains(mob.GameObjectId)
                          && this.EvaluateMobEligibility(mob, player.GameObjectId).IsEligible)
            .OrderBy(mob => Vector3.DistanceSquared(player.Position, mob.Position))
            .FirstOrDefault();
    }

    private void LogTargetDetectionSnapshot(
        MobTargetPreset target,
        IReadOnlyList<IBattleNpc> nearby,
        Vector3 playerPosition,
        ulong playerObjectId)
    {
        DateTime now = DateTime.UtcNow;
        if (now < this.nextTargetDetectionDiagnosticAt)
            return;
        this.nextTargetDetectionDiagnosticAt = now.AddSeconds(2);

        var observations = nearby
            .Select(mob =>
            {
                bool friendly = this.nativeGameState.IsFriendly(mob);
                bool valid = mob.IsValid();
                bool attackable = mob.BattleNpcKind == BattleNpcSubKind.Combatant
                    && !mob.IsDead
                    && mob.IsTargetable
                    && valid
                    && !friendly;
                bool matchingName = mob.NameId == target.BNpcNameId;
                bool skipped = this.skippedTargetObjectIds.Contains(mob.GameObjectId);
                MobEligibility eligibility = this.EvaluateMobEligibility(mob, playerObjectId);
                bool candidate = attackable && matchingName && !skipped && eligibility.IsEligible;
                string reason = candidate
                    ? "候选"
                    : string.Join(",", new[]
                    {
                        !matchingName ? $"NameId不符(目标={target.BNpcNameId})" : string.Empty,
                        mob.BattleNpcKind != BattleNpcSubKind.Combatant ? $"BattleNpcKind={mob.BattleNpcKind}" : string.Empty,
                        mob.IsDead ? "已死亡" : string.Empty,
                        !mob.IsTargetable ? "不可选中" : string.Empty,
                        !valid ? "IsValid=false" : string.Empty,
                        friendly ? "友方判定" : string.Empty,
                        skipped ? "本轮已跳过" : string.Empty,
                        !eligibility.IsEligible ? eligibility.Reason : string.Empty,
                    }.Where(flag => !string.IsNullOrEmpty(flag)));

                return new
                {
                    Mob = mob,
                    Distance = Vector3.Distance(playerPosition, mob.Position),
                    MatchingName = matchingName,
                    Skipped = skipped,
                    Candidate = candidate,
                    Reason = reason,
                    FateId = eligibility.NativeState.FateId,
                    CombatTagType = eligibility.NativeState.CombatTagType,
                    CombatTaggerId = eligibility.NativeState.CombatTaggerId,
                };
            })
            .OrderBy(observation => observation.Distance)
            .ToArray();

        string nearbyText = observations.Length == 0
            ? "（无 IBattleNpc）"
            : string.Join(" | ", observations.Select(observation =>
                $"id=0x{observation.Mob.GameObjectId:X},NameId={observation.Mob.NameId},"
                + $"name=\"{observation.Mob.Name.TextValue}\",d={observation.Distance:0.0},"
                + $"target=0x{observation.Mob.TargetObjectId:X},dead={observation.Mob.IsDead},"
                + $"targetable={observation.Mob.IsTargetable},FateId={observation.FateId},"
                + $"CombatTagType={observation.CombatTagType},CombatTaggerId=0x{observation.CombatTaggerId:X},"
                + $"reason={observation.Reason}"));
        this.AddDiagnostic(
            DiagnosticSeverity.Debug,
            $"目标探测快照：目标 NameId={target.BNpcNameId}，目标 TerritoryTypeId={target.TerritoryTypeId}，"
            + $"对象表 IBattleNpc={nearby.Count}（无距离裁剪），"
            + $"UI名称过滤=\"{this.configuration.NameFilter}\"，UI最大显示数={this.configuration.MaxTrackedMobs}：{nearbyText}");

        string candidateText = observations.Any(observation => observation.Candidate)
            ? string.Join(" | ", observations.Where(observation => observation.Candidate).Select(observation =>
                $"id=0x{observation.Mob.GameObjectId:X},NameId={observation.Mob.NameId},name=\"{observation.Mob.Name.TextValue}\",d={observation.Distance:0.0}"))
            : "（无符合条件的候选）";
        string scanText = this.mobs.Count == 0
            ? "（UI 扫描候选为空）"
            : string.Join(" | ", this.mobs.Select(mob =>
                $"id=0x{mob.GameObjectId:X},NameId={mob.BNpcNameId},name=\"{mob.Name}\",d={mob.Distance:0.0}"));
        this.AddDiagnostic(DiagnosticSeverity.Debug, $"目标候选名单：{candidateText}");
        this.AddDiagnostic(DiagnosticSeverity.Debug, $"UI 扫描候选名单：{scanText}");
    }

    private void ReportTargetDetectionSkip(string reason)
    {
        DateTime now = DateTime.UtcNow;
        if (now < this.nextTargetDetectionDiagnosticAt)
            return;
        this.nextTargetDetectionDiagnosticAt = now.AddSeconds(2);
        this.AddDiagnostic(DiagnosticSeverity.Debug, reason);
    }

    private bool TryInterruptCruiseForTarget(DateTime now)
    {
        if (this.configuration.GetActivePresetList().Targets.Count == 0)
            return false;
        if (this.State != AutomationState.NavigatingToSpawnPoint)
            return false;
        if (this.travelSessionActive
            && this.travelSession.State != FieldNavigation.NavigationTravelState.Navigating)
            return false;
        MobTargetPreset target = this.CurrentTargetSpec();
        if (this.TryFindTargetMob(target) is not { } mob)
            return false;

        this.BeginTarget(mob, now);
        return true;
    }

    private static TargetApproachMode GetTargetApproachMode(Vector3 player, Vector3 target)
    {
        if (MathF.Abs(player.Y - target.Y) > 3f)
            return TargetApproachMode.Fly;

        float distance = HorizontalDistance(player, target);
        return distance <= GroundWalkDistance
            ? TargetApproachMode.Walk
            : distance <= GroundMountDistance
                ? TargetApproachMode.GroundMount
                : TargetApproachMode.Fly;
    }

    private static string TargetApproachLabel(TargetApproachMode mode) => mode switch
    {
        TargetApproachMode.Walk => "步行",
        TargetApproachMode.GroundMount => "地面坐骑",
        TargetApproachMode.Fly => "飞行",
        _ => "导航",
    };

    private void TriggerForcedFlightReposition(DateTime now, string reason)
    {
        this.StopMovement();
        this.forceCombatReposition = true;
        this.combatRepositionAttempted = true;
        this.targetApproachMode = TargetApproachMode.Fly;
        this.combatWaitDeadline = DateTime.MinValue;
        this.SetState(AutomationState.PreparingTargetApproach, reason);
    }

    private IBattleNpc? FindAggroTarget()
    {
        IPlayerCharacter? player = this.objectTable.LocalPlayer;
        if (player is null)
            return null;
        return this.objectTable.OfType<IBattleNpc>()
            .Where(IsAttackableMob)
            .Where(mob => Vector3.Distance(player.Position, mob.Position) <= AggroSearchRadius)
            .Where(mob => mob.TargetObjectId == player.GameObjectId
                          || (this.condition[ConditionFlag.InCombat] && this.nativeGameState.IsInCombat(mob)))
            .OrderBy(mob => Vector3.DistanceSquared(player.Position, mob.Position))
            .FirstOrDefault();
    }

    private IBattleNpc? FindBattleNpc(ulong objectId) => objectId == 0
        ? null
        : this.objectTable.OfType<IBattleNpc>().FirstOrDefault(mob => mob.GameObjectId == objectId);

    private MobTargetPreset CurrentTargetSpec()
    {
        MobGrinderPreset preset = this.configuration.GetActivePresetList();
        return preset.Targets[Math.Clamp(this.targetIndex, 0, preset.Targets.Count - 1)];
    }

    private IReadOnlyList<Vector3> GetCurrentSpawnPoints()
    {
        MobTargetPreset? target = this.configuration.GetActivePresetList().Targets.ElementAtOrDefault(this.targetIndex);
        return target is null
            ? Array.Empty<Vector3>()
            : this.spawnData.GetWorldPointsByNameAndTerritory(target.BNpcNameId, target.TerritoryTypeId);
    }

    private MobSelectionEntry? GetCurrentTarget()
    {
        MobTargetPreset? target = this.configuration.GetActivePresetList().Targets.ElementAtOrDefault(this.targetIndex);
        return target is null
            ? null
            : this.spawnData.MobTargets.FirstOrDefault(entry => entry.BNpcNameId == target.BNpcNameId && entry.TerritoryTypeId == target.TerritoryTypeId);
    }

    private void Scan(DateTime now)
    {
        this.nextScanAt = now + ScanInterval;
        if (!this.clientState.IsLoggedIn || !this.playerState.IsLoaded || this.objectTable.LocalPlayer is not { } player)
        {
            this.mobs = [];
            return;
        }
        this.LastScanAt = now;
        this.mobs = this.objectTable.OfType<IBattleNpc>()
            .Where(mob => this.EvaluateMobEligibility(mob, player.GameObjectId).IsEligible)
            .Where(mob => string.IsNullOrWhiteSpace(this.configuration.NameFilter)
                          || mob.Name.TextValue.Contains(this.configuration.NameFilter, StringComparison.CurrentCultureIgnoreCase))
            .Select(mob => new MobSnapshot(
                mob.GameObjectId,
                mob.NameId,
                mob.Name.TextValue,
                mob.Position,
                Vector3.Distance(player.Position, mob.Position),
                mob.CurrentHp,
                mob.MaxHp,
                this.nativeGameState.IsInCombat(mob)))
            .OrderBy(mob => mob.Distance)
            .Take(this.configuration.MaxTrackedMobs)
            .ToArray();
    }

    private void ResetRuntime()
    {
        this.targetIndex = 0;
        this.spawnPointIndex = 0;
        this.currentTargetKillCount = 0;
        this.selectedTargetId = 0;
        this.selectedTargetWasEngaged = false;
        this.targetInterruptedBeforeArrival = false;
        this.combatWaitDeadline = DateTime.MinValue;
        this.forceCombatReposition = false;
        this.combatRepositionAttempted = false;
        this.targetApproachMode = TargetApproachMode.Walk;
        this.walkSprintAttempted = false;
        this.skippedTargetObjectIds.Clear();
        this.nextMountAttemptAt = DateTime.MinValue;
        this.nextDismountAttemptAt = DateTime.MinValue;
        this.spawnPoints = this.GetCurrentSpawnPoints();
        this.currentHoverResolved = false;
        this.spawnPointHoverDestinations = [];
        this.spawnPointHeightsResolved = false;
        this.teleportPlan = null;
        this.teleportRequestedAt = DateTime.MinValue;
        this.teleportCompletedAt = DateTime.MinValue;
        this.EndTravelSession();
        this.landing.StopDescending();
    }

    private void StopMovement()
    {
        this.EndTravelSession();
        this.vnavmesh.Stop();
        this.landing.StopDescending();
    }

    private void StartTravelSession(
        DateTime now,
        Vector3 destination,
        FieldNavigation.NavigationTravelIntent intent,
        FieldNavigation.GroundDestinationKind groundKind,
        string context)
    {
        if (this.travelSessionActive)
            return;

        IPlayerCharacter? player = this.objectTable.LocalPlayer;
        if (player is null)
            return;

        FieldNavigation.NoFlyZone? zone = this.noFlyZones.Find(this.clientState.TerritoryType, player.Position);
        this.travelContext = context;
        this.travelSession.Start(
            now,
            player.Position,
            destination,
            zone is not null,
            new FieldNavigation.NavigationTravelOptions
            {
                InitialIntent = intent,
                GroundKind = groundKind,
                HorizontalProgress = intent == FieldNavigation.NavigationTravelIntent.Ground,
                StallTimeout = TimeSpan.FromSeconds(GroundNavigationStallSeconds),
            });
        this.travelSessionActive = true;
        this.nextNavigationSnapshotAt = DateTime.MinValue;
    }

    private void EndTravelSession()
    {
        if (!this.travelSessionActive)
            return;
        this.travelSession.Cancel();
        this.travelSessionActive = false;
        this.travelContext = string.Empty;
        this.nextNavigationSnapshotAt = DateTime.MinValue;
    }

    private FieldNavigation.NavigationTravelUpdate TickTravelSession(
        DateTime now,
        Vector3 destination,
        Func<bool> arrived)
    {
        IPlayerCharacter? player = this.objectTable.LocalPlayer;
        if (player is null || !this.travelSessionActive)
            throw new InvalidOperationException("导航会话尚未启动");

        FieldNavigation.NoFlyZone? zone = this.noFlyZones.Find(this.clientState.TerritoryType, player.Position);
        FieldNavigation.FlightState flightState = new(
            this.mount.IsInFlight,
            this.mount.IsJumping,
            this.mount.IsMountTransition);
        bool arrivedNow = arrived();
        FieldNavigation.NavigationTravelUpdate update = this.travelSession.Tick(
            new FieldNavigation.NavigationTravelFrame(
                now,
                player.Position,
                Vector3.Distance(player.Position, destination),
                arrivedNow,
                zone is not null,
                flightState));

        if (now >= this.nextNavigationSnapshotAt)
        {
            TimeSpan settledFor = this.travelSession.Landing.SettledSince is { } settledSince
                ? now - settledSince
                : TimeSpan.Zero;
            this.AddDiagnostic(
                DiagnosticSeverity.Debug,
                $"[NAV_SNAPSHOT] context={this.travelContext}; businessState={this.State}; "
                + $"sessionState={update.State}; outcome={update.Outcome}; initialIntent={this.travelSession.InitialIntent}; "
                + $"mode={update.Mode}; groundKind={this.travelSession.GroundKind}; "
                + $"groundReason={update.GroundReason}; failureStage={update.FailureStage}; "
                + $"failureRejection={this.travelSession.FailureRejection}; request={update.RequestResult}; "
                + $"requestRejection={update.Rejection}; insideNoFly={zone is not null}; "
                + $"territory={this.TerritoryId}; targetObjectId=0x{this.selectedTargetId:X}; "
                + $"spawnPoint={this.CurrentSpawnPointNumber}/{this.CurrentSpawnPointCount}; "
                + $"approachMode={this.targetApproachMode}; conditionMounted={this.mount.IsConditionMounted}; "
                + $"nativeMounted={this.mount.IsNativeMounted}; "
                + $"canResumeFlight={this.travelSession.CanResumeFlight}; position={player.Position}; "
                + $"destination={destination}; resolvedDestination={this.travelSession.ResolvedDestination?.ToString() ?? "null"}; "
                + $"requestStartedAt={this.travelSession.RequestStartedAt:O}; "
                + $"distance={Vector3.Distance(player.Position, destination):0.0}; "
                + $"arrived={arrivedNow}; inFlight={flightState.InFlight}; jumping={flightState.Jumping}; "
                + $"mountTransition={flightState.MountTransition}; moveInProgress={this.vnavmesh.IsMoveInProgress}; "
                + $"pathRunning={this.vnavmesh.IsPathRunning}; moveActive={this.vnavmesh.IsMoveActive}; "
                + $"landingStatus={update.LandingStatus}; settledForMs={settledFor.TotalMilliseconds:0}; "
                + $"reason={update.Reason}");
            this.nextNavigationSnapshotAt = now.AddSeconds(1);
        }
        if (update.State is FieldNavigation.NavigationTravelState.Landing
            or FieldNavigation.NavigationTravelState.JumpRecovery)
        {
            this.StatusReason = $"{this.travelContext}：{update.Reason}";
        }
        else if (update.Outcome == FieldNavigation.NavigationTravelOutcome.Progress
                 && !string.IsNullOrWhiteSpace(update.Reason))
        {
            this.StatusReason = update.Reason;
        }
        return update;
    }

    private void BeginWaitingForCombat(string reason)
    {
        this.combatWaitDeadline = DateTime.UtcNow.AddSeconds(10);
        this.SetState(AutomationState.WaitingForCombat, reason);
    }

    private void SetState(AutomationState state, string reason)
    {
        bool changed = this.State != state;
        if (!changed && this.StatusReason == reason)
            return;
        this.State = state;
        this.StatusReason = reason;
        this.stateStartedAt = DateTime.UtcNow;
        if (state is AutomationState.Teleporting or AutomationState.WaitingForTerritory)
            this.teleportCompletedAt = DateTime.MinValue;
        this.AddDiagnostic(
            changed ? DiagnosticSeverity.Information : DiagnosticSeverity.Debug,
            changed ? $"状态 → {state}：{reason}" : $"{state}：{reason}");
    }

    private void AddDiagnostic(DiagnosticSeverity severity, string message)
    {
        lock (this.diagnosticsLock)
        {
            this.diagnostics.Enqueue(new DiagnosticEntry(DateTime.Now, severity, message));
            while (this.diagnostics.Count > MaxDiagnostics)
                this.diagnostics.Dequeue();
        }

        switch (severity)
        {
            case DiagnosticSeverity.Error:
                this.log.Error("{Message}", message);
                break;
            case DiagnosticSeverity.Warning:
                this.log.Warning("{Message}", message);
                break;
            case DiagnosticSeverity.Information:
                this.log.Information("{Message}", message);
                break;
            default:
                this.log.Debug("{Message}", message);
                break;
        }
    }

    private void FailAutomation(string reason)
    {
        this.StopMovement();
        this.configuration.Enabled = false;
        this.SetState(AutomationState.Stopped, reason);
    }

    private bool IsBetweenAreas() => this.condition[ConditionFlag.BetweenAreas] || this.condition[ConditionFlag.BetweenAreas51];

    private bool IsAttackableMob(IBattleNpc mob)
    {
        if (mob.BattleNpcKind != BattleNpcSubKind.Combatant || mob.IsDead || !mob.IsTargetable || !mob.IsValid())
            return false;
        return !this.nativeGameState.IsFriendly(mob);
    }

    private MobEligibility EvaluateMobEligibility(IBattleNpc mob, ulong playerObjectId)
    {
        NativeMobState nativeState = this.nativeGameState.ReadMobState(mob);
        if (!IsAttackableMob(mob))
            return new(false, "基础条件不满足", nativeState);

        if (nativeState.FateId != 0)
            return new(false, $"属于 FATE（FateId={nativeState.FateId}）", nativeState);

        if (nativeState.CombatTaggerId == 0 || this.IsOurCombatTagger(nativeState.CombatTaggerId, playerObjectId))
            return new(true, "未被其他玩家占有", nativeState);

        IGameObject? tagger = this.objectTable.SearchById(nativeState.CombatTaggerId);
        if (tagger is IPlayerCharacter)
            return new(false, $"战斗标签属于其他玩家（CombatTaggerId=0x{nativeState.CombatTaggerId:X}）", nativeState);

        if (tagger is null)
            return new(false, $"无法确认战斗标签归属（CombatTaggerId=0x{nativeState.CombatTaggerId:X}）", nativeState);

        // A non-player tagger (for example an NPC) does not make the target an
        // exclusive player claim. It may still be valid for us to finish it.
        return new(true, $"战斗标签来自非玩家对象（CombatTaggerId=0x{nativeState.CombatTaggerId:X}）", nativeState);
    }

    private bool IsOurCombatTagger(ulong taggerId, ulong playerObjectId)
    {
        if (taggerId == playerObjectId)
            return true;

        return this.partyList.Any(member => member.EntityId != 0 && member.EntityId == taggerId);
    }

    private static float HorizontalDistance(Vector3 left, Vector3 right) =>
        Vector2.Distance(new Vector2(left.X, left.Z), new Vector2(right.X, right.Z));

    private bool IsAtCurrentHover(Vector3 position) =>
        this.currentHoverResolved
        && HorizontalDistance(position, this.currentHoverDestination) <= this.configuration.SpawnPointArrivalRadius
        && MathF.Abs(position.Y - this.currentHoverDestination.Y) <= this.configuration.FlightHeight + 10f;

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
