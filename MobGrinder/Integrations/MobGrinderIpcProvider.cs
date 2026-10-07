using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;

namespace MobGrinder;

/// <summary>
/// Public IPC surface for one-shot mob objectives. The contract uses only primitive values so
/// callers do not need a reference to MobGrinder's assembly.
/// </summary>
public sealed class MobGrinderIpcProvider : IDisposable
{
    private readonly ICallGateProvider<uint, uint, int, bool> startSingleTarget;
    private readonly ICallGateProvider<object> stop;
    private readonly ICallGateProvider<string> getState;
    private readonly ICallGateProvider<string> getStatus;
    private readonly ICallGateProvider<bool> isRunning;
    private readonly ICallGateProvider<int> getCurrentCount;
    private readonly ICallGateProvider<int> getRequiredCount;
    private readonly ICallGateProvider<string> getResult;
    private readonly MobGrinderController controller;
    private readonly IPluginLog log;
    private bool disposed;

    public MobGrinderIpcProvider(IDalamudPluginInterface pluginInterface, MobGrinderController controller,
        IPluginLog log)
    {
        this.controller = controller;
        this.log = log;
        this.startSingleTarget = pluginInterface.GetIpcProvider<uint, uint, int, bool>("MobGrinder.StartSingleTarget");
        this.stop = pluginInterface.GetIpcProvider<object>("MobGrinder.Stop");
        this.getState = pluginInterface.GetIpcProvider<string>("MobGrinder.GetState");
        this.getStatus = pluginInterface.GetIpcProvider<string>("MobGrinder.GetStatus");
        this.isRunning = pluginInterface.GetIpcProvider<bool>("MobGrinder.IsRunning");
        this.getCurrentCount = pluginInterface.GetIpcProvider<int>("MobGrinder.GetCurrentCount");
        this.getRequiredCount = pluginInterface.GetIpcProvider<int>("MobGrinder.GetRequiredCount");
        this.getResult = pluginInterface.GetIpcProvider<string>("MobGrinder.GetResult");

        this.startSingleTarget.RegisterFunc(this.HandleStartSingleTarget);
        this.stop.RegisterAction(this.HandleStop);
        this.getState.RegisterFunc(() => controller.State.ToString());
        this.getStatus.RegisterFunc(() => controller.StatusReason);
        this.isRunning.RegisterFunc(() => controller.State is not AutomationState.Stopped);
        this.getCurrentCount.RegisterFunc(() => controller.CurrentTargetKillCount);
        this.getRequiredCount.RegisterFunc(() => controller.SingleTargetRequiredCount);
        this.getResult.RegisterFunc(() => controller.SingleTargetResult);
        log.Information("MobGrinder IPC Provider 已注册：StartSingleTarget、Stop、GetState、GetStatus、IsRunning、GetCurrentCount、GetRequiredCount、GetResult");
    }

    private bool HandleStartSingleTarget(uint bnpcNameId, uint territoryTypeId, int requiredCount)
    {
        this.log.Information(
            "MobGrinder IPC 收到 StartSingleTarget：BNpcNameId={BNpcNameId}，地图={TerritoryTypeId}，数量={RequiredCount}；当前State={State}，SingleTargetActive={Active}",
            bnpcNameId, territoryTypeId, requiredCount, this.controller.State, this.controller.IsSingleTargetActive);
        bool accepted = this.controller.StartSingleTarget(bnpcNameId, territoryTypeId, requiredCount);
        this.log.Information(
            "MobGrinder IPC StartSingleTarget 返回：Accepted={Accepted}；BNpcNameId={BNpcNameId}，地图={TerritoryTypeId}，数量={RequiredCount}",
            accepted, bnpcNameId, territoryTypeId, requiredCount);
        return accepted;
    }

    private void HandleStop()
    {
        this.log.Information("MobGrinder IPC 收到 Stop：当前State={State}，SingleTargetActive={Active}，Result={Result}",
            this.controller.State, this.controller.IsSingleTargetActive, this.controller.SingleTargetResult);
        this.controller.Stop();
    }

    public void Dispose()
    {
        if (this.disposed)
            return;

        this.disposed = true;
        this.startSingleTarget.UnregisterFunc();
        this.stop.UnregisterAction();
        this.getState.UnregisterFunc();
        this.getStatus.UnregisterFunc();
        this.isRunning.UnregisterFunc();
        this.getCurrentCount.UnregisterFunc();
        this.getRequiredCount.UnregisterFunc();
        this.getResult.UnregisterFunc();
    }
}
