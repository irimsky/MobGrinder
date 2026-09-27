using System.Numerics;

namespace MobGrinder;

/// <summary>
/// Immutable data copied from a live game object during one scan.
/// The controller deliberately does not retain live game-object wrappers across frames.
/// </summary>
public sealed record MobSnapshot(
    ulong GameObjectId,
    uint BNpcNameId,
    string Name,
    Vector3 Position,
    float Distance,
    uint CurrentHp,
    uint MaxHp,
    bool IsInCombat);
