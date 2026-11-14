using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Bloop.Client.Desktop
{
    // Desktop sign-in: listens on a loopback port, opens the system browser, and completes
    // when the browser is redirected back to that port.
    public sealed class LoopbackAuthBrowser(Action<string>? openBrowser = null, TimeSpan? timeout = null) : IAuthBrowser
    {
        private readonly Action<string> _openBrowser = openBrowser ?? OpenInDefaultBrowser;
        private readonly TimeSpan _timeout = timeout ?? TimeSpan.FromMinutes(5);

        public Task<IAuthBrowserSession> BeginAsync(CancellationToken cancellationToken = default)
        {
            // Another process could claim a port between choosing it and listening on it,
            // so retry with a fresh one.
            for (var attempt = 0; ; attempt++)
            {
                var port = FindFreePort();
                var listener = new HttpListener();
                listener.Prefixes.Add($"http://127.0.0.1:{port}/callback/");

                try
                {
                    listener.Start();
                    return Task.FromResult<IAuthBrowserSession>(
                        new Session(listener, $"http://127.0.0.1:{port}/callback", _openBrowser, _timeout));
                }
                catch (HttpListenerException) when (attempt < 5)
                {
                    listener.Close();
                }
            }
        }

        private static int FindFreePort()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            try
            {
                return ((IPEndPoint)probe.LocalEndpoint).Port;
            }
            finally
            {
                probe.Stop();
            }
        }

        private static void OpenInDefaultBrowser(string address) =>
            Process.Start(new ProcessStartInfo(address) { UseShellExecute = true });

        private sealed class Session(
            HttpListener listener, string redirectUri, Action<string> openBrowser, TimeSpan timeout) : IAuthBrowserSession
        {
            private const string PageStart =
                "<!doctype html><html><head><meta charset=\"utf-8\"><title>Sign-in</title>" +
                "<style>body{font-family:system-ui,sans-serif;margin:15vh auto;max-width:28rem;text-align:center;color:#222}" +
                "@media(prefers-color-scheme:dark){body{background:#1c1c1e;color:#eee}}</style></head><body><h2>";

            private const string PageEnd =
                "</h2><p>You can close this window and return to the app.</p></body></html>";

            public string RedirectUri => redirectUri;

            public async Task<IReadOnlyDictionary<string, string>> OpenAndWaitAsync(
                string address, CancellationToken cancellationToken = default)
            {
                openBrowser(address);

                using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutSource.CancelAfter(timeout);

                HttpListenerContext context;
                try
                {
                    context = await listener.GetContextAsync().WaitAsync(timeoutSource.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new TimeoutException("Sign-in was not completed in the browser in time");
                }

                var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var key in context.Request.QueryString.AllKeys)
                {
                    if (key != null)
                        parameters[key] = context.Request.QueryString[key] ?? "";
                }

                var failed = parameters.ContainsKey("error");
                var body = Encoding.UTF8.GetBytes(
                    PageStart + (failed ? "Sign-in was not completed" : "You are signed in") + PageEnd);

                context.Response.ContentType = "text/html; charset=utf-8";
                context.Response.ContentLength64 = body.Length;
                await context.Response.OutputStream.WriteAsync(body, cancellationToken);
                context.Response.Close();

                return parameters;
            }

            public ValueTask DisposeAsync()
            {
                listener.Close();
                return ValueTask.CompletedTask;
            }
        }
    }
}
