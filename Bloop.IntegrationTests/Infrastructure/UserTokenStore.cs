namespace Bloop.IntegrationTests.Infrastructure;

public sealed class UserTokenStore
{
    private readonly Dictionary<string, string?> _tokens = new();

    public void SetToken(string user, string? token) => _tokens[user] = token;

    public string? GetToken(string user) =>
        _tokens.TryGetValue(user, out var token) ? token : null;

    public bool HasUser(string user) => _tokens.ContainsKey(user);

    internal Dictionary<string, string?> Export() => new(_tokens);

    internal void Import(Dictionary<string, string?> data)
    {
        _tokens.Clear();
        foreach (var (key, value) in data)
            _tokens[key] = value;
    }
}
