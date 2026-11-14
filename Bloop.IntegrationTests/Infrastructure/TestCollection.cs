namespace Bloop.IntegrationTests.Infrastructure;

// A named, ordered container of steps. Collections are flattened by the runner so
// each leaf step is reported and checkpointed individually.
public sealed class TestCollection(string name, params ITestStep[] steps) : ITestStep
{
    public string Name => name;

    internal IReadOnlyList<ITestStep> Steps => steps;

    public async Task ExecuteAsync(TestContext context)
    {
        foreach (var step in steps)
            await step.ExecuteAsync(context);
    }
}
