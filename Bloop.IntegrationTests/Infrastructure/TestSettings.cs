namespace Bloop.IntegrationTests.Infrastructure;

public sealed class TestSettings
{
    public required string GatewayUrl { get; init; }
    public required string AdminEmail { get; init; }
    public required string AdminPassword { get; init; }
}
