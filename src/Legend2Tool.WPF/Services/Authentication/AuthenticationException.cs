using System.Net;

namespace Legend2Tool.WPF.Services.Authentication
{
    public sealed class AuthenticationException : Exception
    {
        public AuthenticationException(string message, HttpStatusCode? statusCode = null)
            : base(message)
        {
            StatusCode = statusCode;
        }

        public HttpStatusCode? StatusCode { get; }
    }
}
