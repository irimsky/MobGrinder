using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Control;

namespace MobGrinder;

public sealed unsafe class LandingAdapter : IDisposable, FieldNavigation.IDescentControl
{
    private delegate bool GetInputStatusDelegate(InputManager* manager, InputCode inputCode);

    private readonly Hook<GetInputStatusDelegate> getInputStatusHook;
    private bool descending;

    public LandingAdapter(IGameInteropProvider interop)
    {
        this.getInputStatusHook = interop.HookFromAddress<GetInputStatusDelegate>(
            (nint)InputManager.MemberFunctionPointers.GetInputStatus,
            this.GetInputStatusDetour);
    }

    public bool IsDescending => this.descending;

    public void BeginDescending() => this.descending = true;
    public void StopDescending() => this.descending = false;

    public void Enable() => this.getInputStatusHook.Enable();

    public void Dispose()
    {
        this.descending = false;
        this.getInputStatusHook.Disable();
        this.getInputStatusHook.Dispose();
    }

    private bool GetInputStatusDetour(InputManager* manager, InputCode inputCode) =>
        this.getInputStatusHook.Original(manager, inputCode)
        || (this.descending && inputCode == InputCode.MOVE_DESCENT);
}
