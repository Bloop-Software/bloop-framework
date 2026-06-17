namespace Bloop.IntegrationTests.Infrastructure;

public sealed class TestCollection(string name, params ITestStep[] steps) : ICompositeStep
{
    public string Name => name;

    public async Task ExecuteAsync(TestContext context)
    {
        for (var i = 0; i < steps.Length; i++)
            await steps[i].ExecuteAsync(context);
    }
}
