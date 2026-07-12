using System.Net;

namespace Bloop.WebApi
{
    public class ErrorDetails
    {
        public ErrorDetails(HttpStatusCode statusCode, string message, string? route = null, string? title = null)
        {
            status = (int)statusCode;
            this.title = title ?? statusCode.ToString();
            detail = message;
            instance = route;
        }

        public string type => "https://tools.ietf.org/html/rfc9110#section-15.5";
        public string title { get; set; }
        public int status { get; set; }
        public string detail { get; set; }
        public string? instance { get; set; }
    }
}
