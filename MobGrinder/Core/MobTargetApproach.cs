using System.Numerics;

namespace MobGrinder;

/// <summary>Value snapshot captured when target navigation starts, shared by movement and arrival.</summary>
public readonly record struct MobTargetApproach(Vector3 Destination)
{
    public bool HasArrived(Vector3 playerPosition, float radius) =>
        Vector2.Distance(new(playerPosition.X, playerPosition.Z), new(this.Destination.X, this.Destination.Z))
        <= radius;
}
