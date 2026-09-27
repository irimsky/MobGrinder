using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;

namespace MobGrinder;

public sealed class LifestreamIpc
{
    private readonly ICallGateSubscriber<bool> isBusy;
    private readonly ICallGateSubscriber<uint, byte, bool> teleport;
    private readonly ICallGateSubscriber<object> abort;

    public LifestreamIpc(IDalamudPluginInterface pluginInterface)
    {
        this.isBusy = pluginInterface.GetIpcSubscriber<bool>("Lifestream.IsBusy");
        this.teleport = pluginInterface.GetIpcSubscriber<uint, byte, bool>("Lifestream.Teleport");
        this.abort = pluginInterface.GetIpcSubscriber<object>("Lifestream.Abort");
    }

    public bool IsAvailable
    {
        get { try { return this.isBusy.HasFunction && this.teleport.HasFunction; } catch { return false; } }
    }

    public bool IsBusy
    {
        get { try { return this.isBusy.InvokeFunc(); } catch { return false; } }
    }

    public bool Teleport(uint aetheryteId, byte subIndex = 0)
    {
        try { return this.teleport.InvokeFunc(aetheryteId, subIndex); } catch { return false; }
    }

    public void Abort()
    {
        try { this.abort.InvokeAction(); } catch { }
    }
}
