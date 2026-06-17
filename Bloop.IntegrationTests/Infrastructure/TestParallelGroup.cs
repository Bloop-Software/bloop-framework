namespace Bloop.IntegrationTests.Infrastructure;

public sealed class TestParallelGroup(string name, params ITestStep[] steps) : ICompositeStep
{
    public string Name => name;

    public async Task ExecuteAsync(TestContext context)
    {
        var tasks = steps.Select(s => s.ExecuteAsync(context)).ToArray();
        await Task.WhenAll(tasks);
    }
}
