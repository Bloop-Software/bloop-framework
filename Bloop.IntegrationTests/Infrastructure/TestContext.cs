using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bloop.IntegrationTests.Infrastructure;

public sealed class TestContext
{
    public required GatewayClient Client { get; init; }
    public required TestState State { get; init; }
    public required TestSettings Settings { get; init; }
    public required UserTokenStore Users { get; init; }
    public required ILogger Logger { get; init; }
    public required IServiceScopeFactory ScopeFactory { get; init; }

    internal GroupResumeData? GroupResume { get; set; }

    public AsyncServiceScope CreateScope() => ScopeFactory.CreateAsyncScope();
}
