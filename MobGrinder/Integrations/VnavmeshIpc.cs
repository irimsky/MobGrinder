using System.Numerics;

using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;

namespace MobGrinder;

public sealed class VnavmeshIpc : FieldNavigation.INavigationBackend
{
    private readonly ICallGateSubscriber<bool> isReady;
    private readonly ICallGateSubscriber<Vector3, bool, bool> moveTo;
    private readonly ICallGateSubscriber<bool> moveInProgress;
    private readonly ICallGateSubscriber<bool> pathIsRunning;
    private readonly ICallGateSubscriber<Vector3, float, float, Vector3?> nearestReachablePoint;
    private readonly ICallGateSubscriber<Vector3, bool, float, Vector3?> pointOnFloor;
    private readonly ICallGateSubscriber<object> stopPath;

    public VnavmeshIpc(IDalamudPluginInterface pluginInterface)
    {
        this.isReady = pluginInterface.GetIpcSubscriber<bool>("vnavmesh.Nav.IsReady");
        this.moveTo = pluginInterface.GetIpcSubscriber<Vector3, bool, bool>("vnavmesh.SimpleMove.PathfindAndMoveTo");
        this.moveInProgress = pluginInterface.GetIpcSubscriber<bool>("vnavmesh.SimpleMove.PathfindInProgress");
        this.pathIsRunning = pluginInterface.GetIpcSubscriber<bool>("vnavmesh.Path.IsRunning");
        this.nearestReachablePoint = pluginInterface.GetIpcSubscriber<Vector3, float, float, Vector3?>("vnavmesh.Query.Mesh.NearestPointReachable");
        this.pointOnFloor = pluginInterface.GetIpcSubscriber<Vector3, bool, float, Vector3?>("vnavmesh.Query.Mesh.PointOnFloor");
        this.stopPath = pluginInterface.GetIpcSubscriber<object>("vnavmesh.Path.Stop");
    }

    public bool IsAvailable
    {
        get { try { return this.isReady.InvokeFunc(); } catch { return false; } }
    }

    public bool IsMoveInProgress
    {
        get { try { return this.moveInProgress.InvokeFunc(); } catch { return false; } }
    }

    public bool IsPathRunning
    {
        get { try { return this.pathIsRunning.InvokeFunc(); } catch { return false; } }
    }

    public bool IsMoveActive => this.IsMoveInProgress || this.IsPathRunning;

    public Vector3? FindNearestReachablePoint(Vector3 position, float halfExtentXZ = 20f, float halfExtentY = 100f)
    {
        return this.TryNearestReachablePoint(position, out Vector3 nearest, out _)
            ? nearest
            : null;
    }

    public bool TryResolveTravelGroundDestination(
        Vector3 requested,
        bool mapWaypoint,
        out Vector3 destination,
        out string resolution)
    {
        destination = default;
        resolution = string.Empty;
        string floorFailure = string.Empty;
        if (mapWaypoint
            && this.TryPointOnFloor(new Vector3(requested.X, 1024f, requested.Z), out destination, out floorFailure))
        {
            resolution = "PointOnFloor（moveflag）";
            return true;
        }

        if (mapWaypoint)
            resolution = $"PointOnFloor 未找到地面：{floorFailure}；";
        if (this.TryNearestReachablePoint(requested, out destination, out string nearestFailure))
        {
            resolution += "NearestPointReachable";
            return true;
        }

        resolution += $"NearestPointReachable 未找到地面：{nearestFailure}";
        return false;
    }

    public bool MoveTo(Vector3 destination, bool fly)
    {
        try { return this.moveTo.InvokeFunc(destination, fly); } catch { return false; }
    }

    public bool TryPointOnFloor(Vector3 position, out Vector3 floor, out string failure)
    {
        floor = default;
        try
        {
            Vector3? result = this.pointOnFloor.InvokeFunc(position, true, 5f);
            if (result is not { } value)
            {
                failure = "IPC 返回空值";
                return false;
            }
            if (!IsFinite(value))
            {
                failure = $"IPC 返回无效坐标 {value}";
                return false;
            }
            floor = value;
            failure = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            failure = $"IPC 调用异常：{ex.GetType().Name}: {ex.Message}";
            return false;
        }
    }

    public bool TryNearestReachablePoint(Vector3 position, out Vector3 nearest, out string failure)
    {
        nearest = default;
        try
        {
            Vector3? result = this.nearestReachablePoint.InvokeFunc(position, 30f, 100f);
            if (result is not { } value)
            {
                failure = "IPC 返回空值";
                return false;
            }
            if (!IsFinite(value))
            {
                failure = $"IPC 返回无效坐标 {value}";
                return false;
            }
            nearest = value;
            failure = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            failure = $"IPC 调用异常：{ex.GetType().Name}: {ex.Message}";
            return false;
        }
    }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    public void Stop()
    {
        try { this.stopPath.InvokeAction(); } catch { }
    }
}
