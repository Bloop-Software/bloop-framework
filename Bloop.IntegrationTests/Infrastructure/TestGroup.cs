using System.Security.Cryptography;

namespace Bloop.IntegrationTests.Infrastructure;

public sealed class TestGroup(string name, params ITestStep[] steps) : ICompositeStep
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
