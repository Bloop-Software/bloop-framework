using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bloop.IntegrationTests.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddIntegrationTests(
        this IServiceCollection services, IConfiguration configuration, ITestStep rootStep)
    {
        var settings = configuration.GetSection("TestSettings").Get<TestSettings>()
            ?? throw new InvalidOperationException("TestSettings configuration section is missing");

        services.AddSingleton(settings);
        services.AddSingleton<TestState>();
        services.AddSingleton<UserTokenStore>();
        services.AddSingleton<TestProgressManager>();
        services.AddSingleton(rootStep);

        services.AddHttpClient<GatewayClient>(client =>
        {
            client.BaseAddress = new Uri(settings.GatewayUrl);
        });

        services.AddSingleton(sp => new TestContext
        {
            Client = sp.GetRequiredService<GatewayClient>(),
            State = sp.GetRequiredService<TestState>(),
            Settings = sp.GetRequiredService<TestSettings>(),
            Users = sp.GetRequiredService<UserTokenStore>(),
            Logger = sp.GetRequiredService<ILogger<TestContext>>(),
            ScopeFactory = sp.GetRequiredService<IServiceScopeFactory>(),
        });

        services.AddHostedService<TestRunner>();

        return services;
    }
}
