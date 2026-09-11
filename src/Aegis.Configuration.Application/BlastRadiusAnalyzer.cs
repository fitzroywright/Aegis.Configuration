using Aegis.Configuration.Domain;

namespace Aegis.Configuration.Application;

public sealed class BlastRadiusAnalyzer
{
    public BlastRadiusReport Analyze(DependencyGraph graph, string failedDependencyId)
    {
        Dictionary<string, DependencyNode> nodes = graph.Nodes.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
        List<DependencyImpact> impacts = [];
        Queue<(string Provider, List<string> Path, bool Critical)> queue = new();
        HashSet<string> visited = new(StringComparer.OrdinalIgnoreCase) { failedDependencyId };
        queue.Enqueue((failedDependencyId, [failedDependencyId], true));

        while (queue.Count > 0)
        {
            (string provider, List<string> path, bool inheritedCritical) = queue.Dequeue();
            foreach (DependencyEdge edge in graph.Edges.Where(x => string.Equals(x.ProviderId, provider, StringComparison.OrdinalIgnoreCase)))
            {
                bool critical = inheritedCritical && edge.Critical;
                List<string> nextPath = [.. path, edge.ConsumerId];
                if (nodes.TryGetValue(edge.ConsumerId, out DependencyNode? consumer) && consumer.Kind == DependencyKind.Application)
                {
                    impacts.Add(new(consumer.Id, failedDependencyId, critical, nextPath.Count - 1, nextPath, edge.Purpose));
                }

                if (visited.Add(edge.ConsumerId)) queue.Enqueue((edge.ConsumerId, nextPath, critical));
            }
        }

        DependencyImpact[] ordered = impacts
            .GroupBy(x => x.ApplicationId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(x => x.Critical).ThenBy(x => x.Distance).First())
            .OrderByDescending(x => x.Critical)
            .ThenBy(x => x.Distance)
            .ThenBy(x => x.ApplicationId)
            .ToArray();
        return new(failedDependencyId, DateTimeOffset.UtcNow, ordered);
    }
}
