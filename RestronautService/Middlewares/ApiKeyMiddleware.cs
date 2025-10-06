using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace RestronautService.Middlewares;

public class ApiKeyMiddleware
{
    private readonly RequestDelegate _next;

    private const string APIKEY_HEADER = "X-Api-Key";
    private const string APIKEY_QUERY = "apiKey";

    public ApiKeyMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Try header first
        if (!context.Request.Headers.TryGetValue(APIKEY_HEADER, out var extractedApiKey))
        {
            // fallback to query string
            if (!context.Request.Query.TryGetValue(APIKEY_QUERY, out extractedApiKey))
            {
                context.Response.StatusCode = 401;
                await context.Response.WriteAsync("Api Key was not provided");
                return;
            }
        }

        var appSettings = context.RequestServices.GetRequiredService<IConfiguration>();
        var apiKey = appSettings.GetValue<string>(APIKEY_HEADER);

        if (!apiKey!.Equals(extractedApiKey))
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsync("Unauthorized client");
            return;
        }

        await _next(context);
    }
}
