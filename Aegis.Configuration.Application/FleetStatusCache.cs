using Aegis.Configuration.Domain;

namespace Aegis.Configuration.Application;

public sealed record FleetStatusSnapshot(DateTimeOffset CapturedAtUtc, IReadOnlyList<CommissioningSnapshot> Applications);

public sealed class FleetStatusCache
{
    private readonly object gate = new();
    private FleetStatusSnapshot? current;

    public FleetStatusSnapshot? Current
    {
        get { lock (gate) return current; }
    }

    public FleetStatusSnapshot Replace(IReadOnlyList<CommissioningSnapshot> applications)
    {
        FleetStatusSnapshot next = new(DateTimeOffset.UtcNow, applications);
        lock (gate) current = next;
        return next;
    }

    public void Invalidate()
    {
        lock (gate) current = null;
    }
}
