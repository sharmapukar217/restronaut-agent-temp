
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Extensions.Hosting;
using RestronautService.DataAccess.Remote;

namespace RestronautService.ManualOrderManager;

public class ManualOrderWatcher : BackgroundService
{
    private readonly string watchFolderPath = @"C:/sc/xml/CONFIRM";
    private const int MaxRetries = 3;
    private IVendureData _vendure;

    public ManualOrderWatcher(IVendureData vendureData)
    {
        _vendure = vendureData;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using FileSystemWatcher watcher = WatchForManualOrders(stoppingToken);
            await Task.Delay(1000, stoppingToken);
        }
    }


    private async Task HandleFileEvent(string filePath, CancellationToken stoppingToken)
    {
        for (int retry = 0; retry < MaxRetries; retry++)
        {
            try
            {
                // Read the XML content from the newly created or renamed file
                var xmlContent = await File.ReadAllTextAsync(filePath, stoppingToken);

                // Make a separate POST request to your API
                await _vendure.CreateOrder(new Dictionary<string, string> { { "xml", xmlContent } });

                // Delete the file after processing
                File.Delete(filePath);
                break;
            }
            catch (IOException) when (retry < MaxRetries - 1)
            {
                // Handle file access exceptions (e.g., file in use)
                await Task.Delay(1000, stoppingToken);
            }
            catch (Exception ex)
            {
                // Handle any exceptions (e.g., invalid XML format, API errors)
                Console.WriteLine($"Error processing XML file: {ex.Message}");
            }
        }
    }

    private FileSystemWatcher WatchForManualOrders(CancellationToken stoppingToken)
    {
        // Console.WriteLine("Watching Confirm Folder");
        var watcher = new FileSystemWatcher(watchFolderPath)
        {
            Filter = "OUT*.xml",
            EnableRaisingEvents = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
        };

        watcher.Created += async (sender, e) => await HandleFileEvent(e.FullPath, stoppingToken);
        watcher.Renamed += async (sender, e) => await HandleFileEvent(e.FullPath, stoppingToken);
        return watcher;
    }
}
