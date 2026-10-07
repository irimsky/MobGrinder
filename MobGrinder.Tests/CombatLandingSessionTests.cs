using System.Numerics;

using FieldNavigation;

namespace MobGrinder.Tests;

public sealed class CombatLandingSessionTests
{
    private static readonly DateTime Now = new(2026, 10, 5, 0, 0, 0, DateTimeKind.Utc);
    private static readonly Vector3 Center = new(10f, 5f, 20f);
    private static readonly Vector3 Initial = new(13f, 30f, 24f);

    [Fact]
    public void PointsFollowSymmetryAndRightTriangleInHorizontalPlane()
    {
        Vector3[] points = CombatLandingSession.CreatePoints(Initial, Center, 4f);
        Assert.Equal(Initial, points[0]);
        Assert.Equal(new Vector3(7f, 30f, 16f), points[1]);
        Assert.Equal(new Vector3(6f, 30f, 23f), points[2]);
        Assert.Equal(new Vector3(14f, 30f, 17f), points[3]);
        Vector3 midpoint = (points[0] + points[1]) / 2f;
        Assert.Equal(Center.X, midpoint.X);
        Assert.Equal(Center.Z, midpoint.Z);
        Assert.Equal(0f, Vector3.Dot(points[0] - points[2], points[1] - points[2]));
        Assert.Equal(Vector3.Distance(points[0], points[2]), Vector3.Distance(points[1], points[2]));
        Assert.Equal(midpoint, (points[2] + points[3]) / 2f);
    }

    [Fact]
    public void CenteredInitialPositionDoesNotCollapseAllRecoveryPoints()
    {
        Vector3[] points = CombatLandingSession.CreatePoints(new(10f, 30f, 20f), Center, 4f);
        Assert.Equal(4, points.Distinct().Count());
        Assert.Equal(new Vector3(6f, 30f, 20f), points[1]);
        Assert.Equal(new Vector3(10f, 30f, 24f), points[2]);
        Assert.Equal(new Vector3(10f, 30f, 16f), points[3]);
    }

    [Fact]
    public void BlockedDescentTriesEveryPointOnceThenFails()
    {
        Backend backend = new();
        Descent descent = new();
        CombatLandingSession session = new(backend, descent);
        session.Start(Now, Initial, Center, 4f);
        Vector3 position = Initial;
        DateTime time = Now;
        Assert.Equal(CombatLandingState.Descending, session.Tick(time, position, true).State);
        Assert.True(descent.Active);
        Assert.Equal(CombatLandingState.Descending, session.Tick(time.AddMilliseconds(5999), position, true).State);

        for (int point = 1; point <= 3; point++)
        {
            time = time.AddSeconds(6);
            CombatLandingUpdate blocked = session.Tick(time, position, true);
            Assert.Equal(CombatLandingState.Repositioning, blocked.State);
            Assert.Equal(point, blocked.PointIndex);
            Assert.False(descent.Active);
            session.Tick(time.AddMilliseconds(10), position, true);
            Assert.Equal(session.Destination, backend.Requests[^1]);
            position = session.Destination!.Value;
            time = time.AddSeconds(1);
            Assert.Equal(CombatLandingState.Descending, session.Tick(time, position, true).State);
            Assert.True(descent.Active);
        }
        Assert.Equal(CombatLandingState.Failed, session.Tick(time.AddSeconds(6), position, true).State);
        Assert.False(descent.Active);
        Assert.Equal(3, backend.Requests.Count);
        Assert.Equal(CombatLandingState.Failed, session.Tick(time.AddSeconds(100), position, true).State);
        Assert.Equal(3, backend.Requests.Count);
    }

    [Fact]
    public void DownwardProgressResetsWatchdogButJitterDoesNot()
    {
        CombatLandingSession session = new(new Backend(), new Descent());
        session.Start(Now, Initial, Center, 4f);
        Assert.Equal(CombatLandingState.Descending,
            session.Tick(Now.AddSeconds(5), Initial - Vector3.UnitY, true).State);
        Assert.Equal(CombatLandingState.Descending,
            session.Tick(Now.AddSeconds(10), Initial - Vector3.UnitY, true).State);
        Assert.Equal(CombatLandingState.Repositioning,
            session.Tick(Now.AddSeconds(11), Initial - Vector3.UnitY, true).State);
        session.Start(Now, Initial, Center, 4f);
        Assert.Equal(CombatLandingState.Descending,
            session.Tick(Now.AddSeconds(5), Initial - 0.1f * Vector3.UnitY, true).State);
        Assert.Equal(CombatLandingState.Repositioning,
            session.Tick(Now.AddSeconds(6), Initial, true).State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LandingWinsOverWatchdogDuringDescentOrReposition(bool reposition)
    {
        Backend backend = new();
        Descent descent = new();
        CombatLandingSession session = new(backend, descent);
        session.Start(Now, Initial, Center, 4f);
        session.Tick(Now, Initial, true);
        if (reposition)
        {
            session.Tick(Now.AddSeconds(6), Initial, true);
            session.Tick(Now.AddSeconds(7), Initial, true);
        }
        Assert.Equal(CombatLandingState.Landed, session.Tick(Now.AddSeconds(40), Initial, false).State);
        Assert.False(descent.Active);
        Assert.False(backend.IsMoveActive);
    }

    [Fact]
    public void RejectedAndStuckNavigationAdvanceToNextPointWithoutJumping()
    {
        Backend backend = new() { AcceptRequest = false };
        CombatLandingSession session = new(backend, new Descent());
        session.Start(Now, Initial, Center, 4f);
        session.Tick(Now.AddSeconds(6), Initial, true);
        Assert.Equal(CombatLandingState.Repositioning, session.Tick(Now.AddSeconds(7), Initial, true).State);
        Assert.Equal(2, session.Tick(Now.AddSeconds(12), Initial, true).PointIndex);
        backend.AcceptRequest = true;
        session.Tick(Now.AddSeconds(13), Initial, true);
        Assert.Equal(3, session.Tick(Now.AddSeconds(18), Initial, true).PointIndex);
        Assert.False(backend.IsMoveActive);
    }

    [Fact]
    public void CancelStopsDescentAndDrainsLatePathCalculation()
    {
        Backend backend = new();
        Descent descent = new();
        CombatLandingSession session = new(backend, descent);
        session.Start(Now, Initial, Center, 4f);
        session.Tick(Now, Initial, true);
        session.Cancel();
        Assert.False(descent.Active);
        session.Start(Now, Initial, Center, 4f);
        session.Tick(Now.AddSeconds(6), Initial, true);
        backend.IsMoveInProgress = true;
        session.Tick(Now.AddSeconds(7), Initial, true);
        session.Cancel();
        backend.IsMoveInProgress = false;
        backend.IsPathRunning = true; // SimpleMove publishes its late result after Path.Stop.
        session.PumpCancellation();
        Assert.False(backend.IsMoveActive);
        Assert.Equal(CombatLandingState.Idle, session.State);
        Assert.Null(session.Destination);
    }

    [Fact]
    public void EnteringNoFlyZoneCancelsReposition()
    {
        Backend backend = new();
        CombatLandingSession session = new(backend, new Descent());
        session.Start(Now, Initial, Center, 4f);
        session.Tick(Now.AddSeconds(6), Initial, true);
        session.Tick(Now.AddSeconds(7), Initial, true);
        Assert.Equal(CombatLandingState.Failed, session.Tick(Now.AddSeconds(8), Initial, true, true).State);
        Assert.False(backend.IsMoveActive);
    }

    [Fact]
    public void RepositionUsesBlockedHeightAndCannotCircleIndefinitely()
    {
        CombatLandingSession session = new(new Backend(), new Descent());
        session.Start(Now, Initial, Center, 4f);
        Vector3 position = Initial - 10f * Vector3.UnitY;
        session.Tick(Now.AddSeconds(5), position, true);
        session.Tick(Now.AddSeconds(11), position, true);
        Assert.Equal(position.Y, session.Destination!.Value.Y);
        DateTime time = Now.AddSeconds(11);
        for (int second = 1; second < 30; second++)
        {
            // Keep moving around the wrong place without reaching the next landing point.
            Vector3 circling = position + (second % 2 == 0 ? Vector3.UnitX : -Vector3.UnitX);
            Assert.Equal(1, session.Tick(time.AddSeconds(second), circling, true).PointIndex);
        }
        Assert.Equal(2, session.Tick(time.AddSeconds(30), position, true).PointIndex);
    }

    private sealed class Backend : INavigationBackend
    {
        public bool AcceptRequest { get; set; } = true;
        public bool IsAvailable => true;
        public bool IsMoveInProgress { get; set; }
        public bool IsPathRunning { get; set; }
        public bool IsMoveActive => this.IsMoveInProgress || this.IsPathRunning;
        public List<Vector3> Requests { get; } = [];
        public bool MoveTo(Vector3 destination, bool fly)
        {
            Assert.True(fly);
            this.Requests.Add(destination);
            this.IsPathRunning = this.AcceptRequest;
            return this.AcceptRequest;
        }
        public void Stop() => this.IsPathRunning = false;
        public Vector3? FindNearestReachablePoint(Vector3 position, float halfExtentXZ = 20f,
            float halfExtentY = 100f) => position;
        public bool TryResolveTravelGroundDestination(Vector3 requested, bool mapWaypoint,
            out Vector3 destination, out string resolution)
        {
            destination = requested;
            resolution = string.Empty;
            return true;
        }
    }

    private sealed class Descent : IDescentControl
    {
        public bool Active { get; private set; }
        public void BeginDescending() => this.Active = true;
        public void StopDescending() => this.Active = false;
    }
}
