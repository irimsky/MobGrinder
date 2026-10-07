using System.Numerics;

using FieldNavigation;

namespace MobGrinder.Tests;

public sealed class FieldNavigationIntegrationTests
{
    private static readonly DateTime Now = new(2026, 10, 7, 0, 0, 0, DateTimeKind.Utc);
    private static readonly Vector3 Origin = new(0, 40, 0);
    private static readonly Vector3 Destination = new(200, 40, 0);

    [Fact]
    public void ResolvedGroundEndpointCompletesOnlyAfterPathStops()
    {
        Vector3 resolved = new(180, 5, 0);
        Backend backend = new() { ResolvedFloor = resolved };
        Recovery recovery = new();
        using NavigationTravelSession session = new(backend, new Descent(), recovery);
        session.Start(Now, Origin, Destination, false, new NavigationTravelOptions
        {
            InitialIntent = NavigationTravelIntent.Ground,
            GroundKind = GroundDestinationKind.MapWaypoint,
        });
        Assert.Equal(NavigationRequestResult.Accepted, session.Tick(Frame(Now, Origin, false)).RequestResult);
        Assert.Equal(resolved, backend.Requests.Single().Destination);
        Assert.Equal(NavigationTravelOutcome.Progress,
            session.Tick(Frame(Now.AddSeconds(1), resolved, false)).Outcome);

        // The requested coordinate remains outside arrival range. Reaching the floor
        // selected by vnavmesh must win over the no-path watchdog after it stops.
        backend.IsPathRunning = false;
        NavigationTravelUpdate arrived = session.Tick(Frame(Now.AddSeconds(10), resolved, false));
        Assert.Equal(NavigationTravelOutcome.Arrived, arrived.Outcome);
        Assert.Equal(NavigationTravelState.Completed, arrived.State);
        Assert.Equal(0, recovery.Jumps);
        Assert.Single(backend.Requests);
    }

    [Fact]
    public void EnteringNoFlyZoneResolvesConfiguredFloorAndContinuesOnGround()
    {
        Vector3 configured = new(30, 0, 0);
        Vector3 floor = new(30, 7, 0);
        Backend backend = new() { ResolvedFloor = floor };
        Descent descent = new();
        using NavigationTravelSession session = new(backend, descent);
        session.Start(Now, Origin, Destination, false, new NavigationTravelOptions
        {
            InitialIntent = NavigationTravelIntent.Fly,
        });
        session.Tick(Frame(Now, Origin, true));
        session.Tick(Frame(Now.AddMilliseconds(100), Origin, true, inside: true, configured));
        Assert.Empty(backend.FloorRequests);

        NavigationTravelUpdate entering = session.Tick(
            Frame(Now.AddMilliseconds(1100), Origin, true, inside: true, configured));
        Assert.Equal(NavigationTravelState.Landing, entering.State);
        Assert.Equal(LandingStatus.Approaching, entering.LandingStatus);
        Assert.Equal(configured, backend.FloorRequests.Single());
        Assert.Equal((floor, true), backend.Requests.Last());
        Assert.Equal(0, backend.NearestPointQueries);
        Assert.False(descent.Active);

        Assert.Equal(LandingStatus.Descending,
            session.Tick(Frame(Now.AddSeconds(2), floor, true, inside: true, configured)).LandingStatus);
        Assert.True(descent.Active);
        session.Tick(Frame(Now.AddMilliseconds(2100), floor, false));
        NavigationTravelUpdate landed = session.Tick(Frame(Now.AddMilliseconds(3200), floor, false));
        Assert.Equal(LandingStatus.Landed, landed.LandingStatus);
        Assert.Equal(NavigationTravelMode.Ground, landed.Mode);
        Assert.False(descent.Active);
        session.Tick(Frame(Now.AddMilliseconds(3300), floor, false));
        Assert.False(backend.Requests.Last().Fly);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void ConfiguredLandingRetriesUnavailableFloorOrRejectedPath(bool resolve, bool accept)
    {
        Vector3 configured = new(30, 0, 0);
        Vector3 floor = new(30, 7, 0);
        Backend backend = new() { ResolvedFloor = floor, ResolveFloor = resolve, Accept = accept };
        Descent descent = new();
        LandingSession landing = new(backend, descent);
        landing.SetPreferredDestination(configured);
        Assert.Equal(LandingStatus.WaitingForPath,
            landing.Update(Now, Origin, new FlightState(true, false, false)));
        Assert.False(descent.Active);
        Assert.Equal(0, backend.NearestPointQueries);
        backend.ResolveFloor = backend.Accept = true;
        Assert.Equal(LandingStatus.Approaching,
            landing.Update(Now.AddSeconds(1), Origin, new FlightState(true, false, false)));
        Assert.Equal(floor, landing.Destination);
        Assert.Equal((floor, true), backend.Requests.Last());
        Assert.Equal(0, backend.NearestPointQueries);
    }

    [Fact]
    public void LandingTimeoutFailsAtTwelveSecondsWithoutJumping()
    {
        Backend backend = new();
        Descent descent = new();
        Recovery recovery = new();
        using NavigationTravelSession session = new(backend, descent, recovery);
        session.Start(Now, Origin, Destination, false, new NavigationTravelOptions
        {
            InitialIntent = NavigationTravelIntent.Ground,
        });
        Assert.Equal(NavigationTravelState.Landing, session.Tick(Frame(Now, Origin, true)).State);
        Assert.True(descent.Active);
        Assert.Equal(NavigationTravelState.Landing,
            session.Tick(Frame(Now.AddMilliseconds(11999), Origin, true)).State);
        NavigationTravelUpdate failed = session.Tick(Frame(Now.AddSeconds(12), Origin, true));
        Assert.Equal(NavigationTravelOutcome.Failed, failed.Outcome);
        Assert.Equal(NavigationFailureStage.LandingTimeout, failed.FailureStage);
        Assert.False(descent.Active);
        Assert.Equal(0, recovery.Jumps);
        Assert.Empty(backend.Requests);
    }

    private static NavigationTravelFrame Frame(DateTime time, Vector3 position, bool flight,
        bool inside = false, Vector3? landingPoint = null) => new(
        time, position, Vector3.Distance(position, Destination), false, inside,
        new FlightState(flight, false, false), landingPoint);

    private sealed class Backend : INavigationBackend
    {
        public bool IsAvailable => true;
        public bool IsMoveInProgress => false;
        public bool IsPathRunning { get; set; }
        public bool IsMoveActive => this.IsPathRunning;
        public bool Accept { get; set; } = true;
        public bool ResolveFloor { get; set; } = true;
        public Vector3? ResolvedFloor { get; init; }
        public List<(Vector3 Destination, bool Fly)> Requests { get; } = [];
        public List<Vector3> FloorRequests { get; } = [];
        public int NearestPointQueries { get; private set; }

        public bool MoveTo(Vector3 destination, bool fly)
        {
            this.Requests.Add((destination, fly));
            this.IsPathRunning = this.Accept;
            return this.Accept;
        }

        public void Stop() => this.IsPathRunning = false;

        public Vector3? FindNearestReachablePoint(Vector3 position, float halfExtentXZ = 20f,
            float halfExtentY = 100f)
        {
            this.NearestPointQueries++;
            return position;
        }

        public bool TryResolveTravelGroundDestination(Vector3 requested, bool mapWaypoint,
            out Vector3 destination, out string resolution)
        {
            Assert.True(mapWaypoint);
            this.FloorRequests.Add(requested);
            destination = this.ResolvedFloor ?? requested;
            resolution = "Test floor";
            return this.ResolveFloor;
        }
    }

    private sealed class Descent : IDescentControl
    {
        public bool Active { get; private set; }
        public void BeginDescending() => this.Active = true;
        public void StopDescending() => this.Active = false;
    }

    private sealed class Recovery : ITravelRecoveryControl
    {
        public int Jumps { get; private set; }
        public bool TryJump() { this.Jumps++; return true; }
    }
}
