using Aegis.Configuration.Domain;

namespace Aegis.Configuration.Application;

public sealed class BlastRadiusAnalyzer
{
    public BlastRadiusReport Analyze(DependencyGraph graph, string failedDependencyId)
    {
        Dictionary<string, DependencyNode> nodes = graph.Nodes.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
        List<DependencyImpact> impacts = [];
        Queue<(string Provider, List<string> Path, bool Critical)> queue = new();
        HashSet<string> visitedStates = new(StringComparer.OrdinalIgnoreCase);
        queue.Enqueue((failedDependencyId, [failedDependencyId], true));
        visitedStates.Add(State(failedDependencyId, true));

        while (queue.Count > 0)
        {
            (string provider, List<string> path, bool inheritedCritical) = queue.Dequeue();
            foreach (DependencyEdge edge in graph.Edges.Where(x => string.Equals(x.ProviderId, provider, StringComparison.OrdinalIgnoreCase)))
            {
                bool critical = inheritedCritical && edge.Critical;
                if (path.Contains(edge.ConsumerId, StringComparer.OrdinalIgnoreCase)) continue;
                List<string> nextPath = [.. path, edge.ConsumerId];
                if (nodes.TryGetValue(edge.ConsumerId, out DependencyNode? consumer) && consumer.Kind == DependencyKind.Application)
                    impacts.Add(new(consumer.Id, failedDependencyId, critical, nextPath.Count - 1, nextPath, edge.Purpose));
                if (visitedStates.Add(State(edge.ConsumerId, critical))) queue.Enqueue((edge.ConsumerId, nextPath, critical));
            }
        }

        DependencyImpact[] ordered = impacts.GroupBy(x => x.ApplicationId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(x => x.Critical).ThenBy(x => x.Distance).First())
            .OrderByDescending(x => x.Critical).ThenBy(x => x.Distance).ThenBy(x => x.ApplicationId).ToArray();
        return new(failedDependencyId, DateTimeOffset.UtcNow, ordered);
    }

    private static string State(string id, bool critical) => $"{id}\u001f{critical}";
}
