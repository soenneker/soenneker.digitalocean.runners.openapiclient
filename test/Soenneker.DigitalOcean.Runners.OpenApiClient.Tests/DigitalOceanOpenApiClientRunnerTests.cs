using Soenneker.Tests.HostedUnit;

namespace Soenneker.DigitalOcean.Runners.OpenApiClient.Tests;

[ClassDataSource<Host>(Shared = SharedType.PerTestSession)]
public sealed class DigitalOceanOpenApiClientRunnerTests : HostedUnitTest
{
    public DigitalOceanOpenApiClientRunnerTests(Host host) : base(host)
    {
    }

    [Test]
    public void Default()
    {

    }
}
