using System.Net;

namespace Bloop.WebApi
{
    public record WebApiResponse<TResponse>(HttpStatusCode StatusCode, TResponse Response)
    {
        public Dictionary<string, string> Headers { get; init; } = new();
    }
}
