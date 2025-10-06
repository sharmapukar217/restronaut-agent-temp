using Refit;
using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RestronautService.Middlewares;
using RestronautService.DataAccess.Remote;
using RestronautService.ManualOrderManager;
using RestronautService.InStoreSalesWatcher;
using RestronautService;
using System.Threading.Tasks;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: true);

var services = builder.Services;
var configuration = builder.Configuration;

services.AddSingleton<IAuthTokenStore, AppSettingsAuthTokenStore>();
services.Configure<HostOptions>(options =>
{
    options.ServicesStartConcurrently = true;
    options.ServicesStopConcurrently = true;
});

services.AddControllers().AddXmlDataContractSerializerFormatters();

var vendureBaseUrl = configuration.GetValue<string>("VendureApiBaseURL");
var vendureAuthToken = configuration.GetValue<string>("VendureApiAuthToken");

if (string.IsNullOrWhiteSpace(vendureBaseUrl) || string.IsNullOrWhiteSpace(vendureAuthToken))
{
    throw new InvalidOperationException("VendureApiBaseURL or VendureApiAuthToken is missing from configuration.");
}

services.AddTransient<GzipRequestHandler>();
services.AddRefitClient<IVendureData>()
    .ConfigureHttpClient(c =>
    {
        c.BaseAddress = new Uri(vendureBaseUrl);
        c.DefaultRequestHeaders.Add("Authorization", vendureAuthToken);
    })
 .AddHttpMessageHandler<GzipRequestHandler>();

services.AddHostedService<ManualOrderWatcher>();
services.AddHostedService<InStoreSalesWatcher>();

var updateUrl = configuration.GetValue<string>("OtaUpdate:Url");
var updateKey = configuration.GetValue<string>("OtaUpdate:Key");

if (!string.IsNullOrEmpty(updateUrl) && !string.IsNullOrEmpty(updateKey))
{
    var updatePattern = configuration.GetValue<string?>("UpdatePattern");
    var otaUpdateUtils = new OtaUpdaterUtils(updateUrl, updateKey, updatePattern);

    services.AddSingleton(otaUpdateUtils);
    services.AddHostedService<OtaUpdaterService>();

    _ = Task.Factory.StartNew(() =>
    {
        while (true)
        {
            try
            {
                if (!Console.IsInputRedirected && Console.KeyAvailable)
                {
                    var key = Console.ReadKey(true);
                    if (key.Key == ConsoleKey.C) Console.Clear();
                    if (key.Key == ConsoleKey.Q) Environment.Exit(0);
                    if (key.Key == ConsoleKey.U && !otaUpdateUtils.isUpgrading)
                    {
                        otaUpdateUtils.ApplyUpdate().Wait();
                    }
                }
                Task.Delay(100).Wait();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Console key listener error: {ex.Message}");
            }
        }
    }, TaskCreationOptions.LongRunning);
}

try
{
    var app = builder.Build();
    app.UseMiddleware<ApiKeyMiddleware>();

    // Configure the HTTP request pipeline.
    app.UseAuthentication();
    app.UseAuthorization();
    app.MapControllers();
    await app.RunAsync();
}
catch (Exception ex)
{
    Console.WriteLine($"Couldn't start the server: {ex.Message}");
}
