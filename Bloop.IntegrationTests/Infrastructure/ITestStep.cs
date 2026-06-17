namespace Bloop.IntegrationTests.Infrastructure;

public interface ITestStep
{
    string Name { get; }
    Task ExecuteAsync(TestContext context);
}

public interface ICompositeStep : ITestStep;
