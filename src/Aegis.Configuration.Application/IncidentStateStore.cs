namespace Aegis.Configuration.Application;

public sealed record ActiveIncident(IncidentSummary Incident, IReadOnlyList<string> FailedDependencies);

public sealed class IncidentStateStore
{
    private readonly object gate = new();
    private ActiveIncident? current;
    public ActiveIncident? Current { get { lock (gate) return current; } }
    public ActiveIncident Publish(IncidentSummary incident, IReadOnlyList<string> failedDependencies)
    {
        ActiveIncident next = new(incident, failedDependencies);
        lock (gate) current = next;
        return next;
    }
    public void Clear() { lock (gate) current = null; }
}
