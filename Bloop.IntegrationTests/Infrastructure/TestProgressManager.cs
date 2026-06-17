using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bloop.IntegrationTests.Infrastructure;

public sealed class GroupResumeData
{
    public required int[] ShuffledIndices { get; set; }
    public required int NextChildIndex { get; set; }
}

public sealed class TestProgressData
{
    public int NextStepIndex { get; set; }
    public GroupResumeData? GroupState { get; set; }
    public Dictionary<string, JsonElement> State { get; set; } = new();
    public Dictionary<string, string?> Users { get; set; } = new();
    public string ActingAs { get; set; } = TestUsers.Anonymous;
}

public sealed class TestProgressManager(string filePath = "test-progress.json")
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public TestProgressData? Load()
    {
        if (!File.Exists(filePath))
            return null;

        var json = File.ReadAllText(filePath);
        return JsonSerializer.Deserialize<TestProgressData>(json, JsonOptions);
    }

    public void Save(TestProgressData data)
    {
        File.WriteAllText(filePath, JsonSerializer.Serialize(data, JsonOptions));
    }

    public void Clear()
    {
        if (File.Exists(filePath))
            File.Delete(filePath);
    }
}
