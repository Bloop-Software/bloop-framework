using System.Net;

namespace Bloop.WebApi
{
    public class WebApiException : Exception
    {
        public HttpStatusCode StatusCode { get; private set; }

        public WebApiException(string message) :
            this(HttpStatusCode.BadRequest, message)
        {
        }

        public WebApiException(HttpStatusCode statusCode, string message) :
            base(message)
        {
            this.StatusCode = statusCode;
        }
    }
}
