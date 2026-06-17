using System.Text.Json;

namespace Bloop.IntegrationTests.Infrastructure;

public sealed class TestState
{
    private readonly Dictionary<string, JsonElement> _data = new();

    public void Set<T>(string key, T value) where T : notnull
    {
        _data[key] = JsonSerializer.SerializeToElement(value);
    }

    public T Get<T>(string key)
    {
        if (!_data.TryGetValue(key, out var element))
            throw new KeyNotFoundException($"Test state key '{key}' not found");

        return element.Deserialize<T>()
            ?? throw new InvalidOperationException($"Test state key '{key}' deserialized to null");
    }

    public bool TryGet<T>(string key, out T? value)
    {
        if (_data.TryGetValue(key, out var element))
        {
            value = element.Deserialize<T>();
            return value != null;
        }

        value = default;
        return false;
    }

    internal Dictionary<string, JsonElement> Export() => new(_data);

    internal void Import(Dictionary<string, JsonElement> data)
    {
        _data.Clear();
        foreach (var (key, value) in data)
            _data[key] = value;
    }
}
