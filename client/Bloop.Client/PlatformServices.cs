namespace Bloop.Client
{
    // Small secrets kept in the platform's credential store (Keychain, DPAPI, Keystore).
    public interface ISecureStore
    {
        Task<string?> GetAsync(string key);

        Task SetAsync(string key, string value);

        Task RemoveAsync(string key);
    }

    // Runs a sign-in or consent flow in the user's own browser and hands back the result.
    // The redirect address has to exist before the flow starts, because the server needs
    // it to build the provider's authorization address.
    public interface IAuthBrowser
    {
        Task<IAuthBrowserSession> BeginAsync(CancellationToken cancellationToken = default);
    }

    public interface IAuthBrowserSession : IAsyncDisposable
    {
        string RedirectUri { get; }

        // Opens the address in the browser and completes with the query parameters the
        // browser was finally redirected back with.
        Task<IReadOnlyDictionary<string, string>> OpenAndWaitAsync(
            string address, CancellationToken cancellationToken = default);
    }

    public sealed class InMemorySecureStore : ISecureStore
    {
        private readonly Dictionary<string, string> _values = [];

        public Task<string?> GetAsync(string key) =>
            Task.FromResult(_values.TryGetValue(key, out var value) ? value : null);

        public Task SetAsync(string key, string value)
        {
            _values[key] = value;
            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key)
        {
            _values.Remove(key);
            return Task.CompletedTask;
        }
    }
}
