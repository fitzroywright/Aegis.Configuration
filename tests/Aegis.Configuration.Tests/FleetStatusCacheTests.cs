using Aegis.Configuration.Application;
using Aegis.Configuration.Domain;

namespace Aegis.Configuration.Tests;

public sealed class FleetStatusCacheTests
{
    [Fact]
    public void Replace_PublishesLatestFleetSnapshot()
    {
        FleetStatusCache cache = new();
        CommissioningSnapshot app = new("app", "1", DateTimeOffset.UtcNow, new("app", "1", []));
        FleetStatusSnapshot published = cache.Replace([app]);
        Assert.Same(published, cache.Current);
        Assert.Single(cache.Current!.Applications);
    }
}
