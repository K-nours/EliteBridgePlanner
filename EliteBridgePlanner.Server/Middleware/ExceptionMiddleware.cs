using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;

namespace EliteBridgePlanner.Server.Middleware;

public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;

    public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (ArgumentException ex)
        {
            // Enum.Parse échoue => 400
            _logger.LogWarning(ex, "Valeur invalide : {Message}", ex.Message);
            await WriteResponse(context, HttpStatusCode.BadRequest, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur non gérée : {Message}", ex.Message);
            var isDev = context.RequestServices.GetService<IWebHostEnvironment>()?.IsDevelopment() == true;
            var message = isDev ? $"{ex.GetType().Name}: {ex.Message}" : "Une erreur interne est survenue.";
            await WriteResponse(context, HttpStatusCode.InternalServerError, message);
        }
    }

    private static async Task WriteResponse(HttpContext ctx, HttpStatusCode code, string message)
    {
        ctx.Response.StatusCode = (int)code;
        ctx.Response.ContentType = "application/json";
        await ctx.Response.WriteAsync(JsonSerializer.Serialize(new { message }));
    }
}
