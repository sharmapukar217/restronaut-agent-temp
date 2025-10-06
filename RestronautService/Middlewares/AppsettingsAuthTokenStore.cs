using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace RestronautService.Middlewares;

public class AppSettingsAuthTokenStore : IAuthTokenStore
{
    private readonly IConfiguration _configuration;

    public AppSettingsAuthTokenStore(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task<string> GetAuthTokenAsync()
    {
        var token = _configuration["AppSettings:VendureApiKey"];

        if (string.IsNullOrEmpty(token))
        {
            throw new InvalidOperationException("Vendure API Key is not configured in appsettings.json.");
        }

        return await Task.FromResult(token);
    }
}
