using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bloop.IntegrationTests.Infrastructure;

public sealed class TestRunner(
    ITestStep rootStep,
    TestContext context,
    TestProgressManager progress,
    IHostApplicationLifetime lifetime,
    ILogger<TestRunner> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var passed = 0;
        var failed = 0;
        var exitCode = 0;

        try
        {
            var saved = progress.Load();
            if (saved != null)
            {
                logger.LogInformation("Resuming from step {Index}", saved.NextStepIndex);
                context.State.Import(saved.State);
                context.Users.Import(saved.Users);
                context.Client.ActingAs = saved.ActingAs;
                context.GroupResume = saved.GroupState;
            }

            var startIndex = saved?.NextStepIndex ?? 0;
            var steps = FlattenSteps(rootStep);

            for (var i = startIndex; i < steps.Count; i++)
            {
                stoppingToken.ThrowIfCancellationRequested();

                var step = steps[i];
                logger.LogInformation("[RUN ] {Name}", step.Name);

                try
                {
                    await step.ExecuteAsync(context);
                    passed++;
                    logger.LogInformation("[PASS] {Name}", step.Name);
                }
                catch (Exception ex)
                {
                    failed++;
                    logger.LogError(ex, "[FAIL] {Name}", step.Name);

                    progress.Save(new TestProgressData
                    {
                        NextStepIndex = i,
                        GroupState = context.GroupResume,
                        State = context.State.Export(),
                        Users = context.Users.Export(),
                        ActingAs = context.Client.ActingAs,
                    });

                    exitCode = 1;
                    break;
                }

                progress.Save(new TestProgressData
                {
                    NextStepIndex = i + 1,
                    State = context.State.Export(),
                    Users = context.Users.Export(),
                    ActingAs = context.Client.ActingAs,
                });
            }

            if (exitCode == 0)
                progress.Clear();
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Test run cancelled");
            exitCode = 1;
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Test runner crashed");
            exitCode = 1;
        }

        logger.LogInformation("Results: {Passed} passed, {Failed} failed", passed, failed);
        Environment.ExitCode = exitCode;
        lifetime.StopApplication();
    }

    private static List<ITestStep> FlattenSteps(ITestStep step)
    {
        if (step is ICompositeStep)
            return [step];

        return [step];
    }
}
