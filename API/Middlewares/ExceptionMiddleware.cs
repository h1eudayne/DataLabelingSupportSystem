using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Net;
using System.Security.Authentication;
using System.Text.Json;

namespace API.Middlewares
{
    public class ExceptionMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ExceptionMiddleware> _logger;
        private readonly IHostEnvironment _env;

        public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger, IHostEnvironment env)
        {
            _next = next;
            _logger = logger;
            _env = env;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                await HandleExceptionAsync(context, ex);
            }
        }

        private async Task HandleExceptionAsync(HttpContext context, Exception ex)
        {
            _logger.LogError(ex, ex.Message);

            context.Response.ContentType = "application/json";

            int statusCode = (int)HttpStatusCode.InternalServerError;
            string message = "Internal Server Error";

            switch (ex)
            {
                case KeyNotFoundException:
                    statusCode = (int)HttpStatusCode.NotFound;
                    message = ex.Message;
                    break;

                case UnauthorizedAccessException:
                case AuthenticationException:
                case SecurityTokenException:
                    statusCode = (int)HttpStatusCode.Unauthorized;
                    message = "Unauthorized access";
                    break;

                case ArgumentException:
                case BadHttpRequestException:
                case FormatException:
                    statusCode = (int)HttpStatusCode.BadRequest;
                    message = ex.Message;
                    break;

                case DbUpdateException:
                    statusCode = (int)HttpStatusCode.Conflict;
                    message = "Database conflict or constraint violation";
                    break;

                case OperationCanceledException:
                    statusCode = 499;
                    message = "Client closed request";
                    break;

                case NotImplementedException:
                    statusCode = (int)HttpStatusCode.NotImplemented;
                    message = "Feature not implemented";
                    break;

                default:
                    statusCode = (int)HttpStatusCode.InternalServerError;
                    message = "An unexpected error occurred";
                    break;
            }

            context.Response.StatusCode = statusCode;

            var response = new
            {
                StatusCode = statusCode,
                Message = message,
                Details = _env.IsDevelopment() ? ex.StackTrace?.ToString() : null
            };

            var json = JsonSerializer.Serialize(response);
            await context.Response.WriteAsync(json);
        }
    }
}
