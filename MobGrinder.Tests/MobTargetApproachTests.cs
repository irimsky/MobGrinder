using System.Numerics;

using FieldNavigation;

namespace MobGrinder.Tests;

public sealed class MobTargetApproachTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReachingOriginalMobPositionCompletesNavigationAfterMobPatrol(bool fly)
    {
        // Positions from the stalled magitek vanguard navigation log: the mob had
        // moved outside the 4-yalm radius, but the player had reached the original endpoint.
        MobTargetApproach approach = new(new Vector3(-205.955f, 78.695724f, -221.98799f));
        Vector3 movedMobPosition = new(-208.14838f, 79.41201f, -227.1001f);
        Vector3 playerPosition = new(-206.21764f, 78.85089f, -223.11115f);
        Assert.True(Vector2.Distance(new(playerPosition.X, playerPosition.Z),
            new(movedMobPosition.X, movedMobPosition.Z)) > 4f);

        NavigationBackend backend = new();
        RecoveryControl recovery = new();
        using NavigationTravelSession session = new(backend, new DescentControl(), recovery);
        DateTime now = new(2026, 10, 4, 0, 0, 0, DateTimeKind.Utc);
        Vector3 origin = new(-112.56701f, 75.96867f, -118.9338f);
        session.Start(now, origin, approach.Destination, insideNoFly: false, new NavigationTravelOptions
        {
            InitialIntent = fly ? NavigationTravelIntent.Fly : NavigationTravelIntent.Ground,
            GroundKind = GroundDestinationKind.LiveObject,
        });
        NavigationTravelUpdate started = session.Tick(new NavigationTravelFrame(
            now, origin, Vector3.Distance(origin, approach.Destination),
            approach.HasArrived(origin, 4f), false, new FlightState(fly, false, false)));
        Assert.Equal(NavigationRequestResult.Accepted, started.RequestResult);
        Assert.Equal(approach.Destination, backend.RequestedDestination);

        // The path has already finished. Even beyond the watchdog deadline, arrival
        // at the snapshot must win over stall recovery and leave combat handoff to the host.
        NavigationTravelUpdate arrived = session.Tick(new NavigationTravelFrame(
            now.AddSeconds(9), playerPosition, Vector3.Distance(playerPosition, approach.Destination),
            approach.HasArrived(playerPosition, 4f), false, new FlightState(fly, false, false)));
        Assert.Equal(NavigationTravelOutcome.Arrived, arrived.Outcome);
        Assert.Equal(NavigationTravelState.Completed, arrived.State);
        Assert.Equal(NavigationFailureStage.None, arrived.FailureStage);
        Assert.Equal(1, backend.MoveRequests);
        Assert.Equal(0, recovery.JumpRequests);
    }

    [Theory]
    [InlineData(4f, true)]
    [InlineData(4.1f, false)]
    public void ArrivalKeepsConfiguredHorizontalRadius(float horizontalDistance, bool expected)
    {
        MobTargetApproach approach = new(new Vector3(10f, 20f, 30f));

        Assert.Equal(expected, approach.HasArrived(new Vector3(10f + horizontalDistance, 50f, 30f), 4f));
    }

    private sealed class NavigationBackend : INavigationBackend
    {
        public bool IsAvailable => true;
        public bool IsMoveInProgress => false;
        public bool IsPathRunning => false;
        public bool IsMoveActive => false;
        public Vector3 RequestedDestination { get; private set; }
        public int MoveRequests { get; private set; }

        public bool MoveTo(Vector3 destination, bool fly)
        {
            this.RequestedDestination = destination;
            this.MoveRequests++;
            return true;
        }

        public void Stop() { }

        public Vector3? FindNearestReachablePoint(Vector3 position, float halfExtentXZ = 20f,
            float halfExtentY = 100f) => position;

        public bool TryResolveTravelGroundDestination(Vector3 requested, bool mapWaypoint,
            out Vector3 destination, out string resolution)
        {
            destination = requested;
            resolution = "Test endpoint";
            return true;
        }
    }

    private sealed class DescentControl : IDescentControl
    {
        public void BeginDescending() { }
        public void StopDescending() { }
    }

    private sealed class RecoveryControl : ITravelRecoveryControl
    {
        public int JumpRequests { get; private set; }

        public bool TryJump()
        {
            this.JumpRequests++;
            return true;
        }
    }
}
