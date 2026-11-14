using System.Security.Cryptography;
using Microsoft.Extensions.Logging;

namespace Bloop.IntegrationTests.Infrastructure;

// Executes its steps in a random order to catch hidden ordering dependencies.
// The runner treats a group as a single unit; on failure the shuffled order is
// logged and persisted so a resumed run continues deterministically.
public sealed class TestGroup(string name, params ITestStep[] steps) : ITestStep
{
    public string Name => name;

    public async Task ExecuteAsync(TestContext context)
    {
        int[] indices;
        var startIndex = 0;

        if (context.GroupResume != null)
        {
            indices = context.GroupResume.ShuffledIndices;
            startIndex = context.GroupResume.NextChildIndex;
            context.GroupResume = null;
        }
        else
        {
            indices = Enumerable.Range(0, steps.Length).ToArray();
            RandomNumberGenerator.Shuffle<int>(indices);
        }

        for (var i = startIndex; i < indices.Length; i++)
        {
            try
            {
                await steps[indices[i]].ExecuteAsync(context);
            }
            catch
            {
                context.Logger.LogError(
                    "Group '{Group}' failed at '{Step}'; shuffled order was [{Order}]",
                    Name, steps[indices[i]].Name,
                    string.Join(", ", indices.Select(x => steps[x].Name)));

                context.GroupResume = new GroupResumeData
                {
                    ShuffledIndices = indices,
                    NextChildIndex = i,
                };
                throw;
            }
        }
    }
}
