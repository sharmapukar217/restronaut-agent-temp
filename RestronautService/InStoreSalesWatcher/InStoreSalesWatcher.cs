
using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using System.Threading;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.Extensions.Hosting;
using RestronautService.DataAccess.Remote;
using System.Xml;
using Amazon.S3.Model;
using Amazon.S3;
using Microsoft.Extensions.Configuration;
using Amazon;

namespace RestronautService.InStoreSalesWatcher;

public class InStoreSalesWatcher : BackgroundService
{
    private const int MaxRetries = 5;

    private readonly string? _storeName;
    private readonly string? _bucketName;
    private readonly IAmazonS3? _s3Client;

    private readonly IVendureData _vendure;
    private readonly string watchFolderPath = @"C:/sc/xml/OUT";

    public InStoreSalesWatcher(IVendureData vendureData, IConfiguration configuration)
    {
        _vendure = vendureData;
        var region = configuration["AWS:Region"];
        var accessKey = configuration["AWS:AccessKey"];
        var secretKey = configuration["AWS:SecretKey"];
        _storeName = configuration["AWS:StoreName"]?.Replace(' ', '_');


        if (
            !string.IsNullOrEmpty(region) &&
            !string.IsNullOrEmpty(accessKey) &&
            !string.IsNullOrEmpty(secretKey)
          )
        {

            _bucketName = configuration["AWS:S3_BucketName"];
            Console.WriteLine($"Using {_bucketName}@{region} for xml storage.");
            var awsCredentials = new Amazon.Runtime.BasicAWSCredentials(accessKey, secretKey);
            _s3Client = new AmazonS3Client(awsCredentials, RegionEndpoint.GetBySystemName(region));
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (Directory.Exists(watchFolderPath))
        {
            var existingFiles = Directory.GetFiles(watchFolderPath, "*.xml");
            foreach (var file in existingFiles)
            {
                _ = Task.Run(() => HandleFileEvent(file, stoppingToken));
            }
        } else
        {
            Directory.CreateDirectory(watchFolderPath);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            using FileSystemWatcher watcher = WatchForInStoreSales(stoppingToken);
            await Task.Delay(1000, stoppingToken);
        }
    }

    private string GenerateS3Key(string fileName)
    {
        var now = DateTime.Now;
        string month = now.ToString("MMMM");
        int weekNumber = (now.Day - 1) / 7 + 1;
        string timestamp = now.ToString("yyyy_MMMM_dd_HH:mm");
        if(_storeName != null)
        {
            return $"{month}/Week_{weekNumber}/{timestamp}_{_storeName}_{fileName}";
        }
        return $"{month}/Week_{weekNumber}/{timestamp}_{fileName}";
    }

    public static string? GetLoyalityMemoCode(XDocument? doc)
    {
        if (doc?.Root == null) return null;

        // Try <Memo>
        string? memo = doc.Descendants("Memo")?.FirstOrDefault()?.Value;
        if (!string.IsNullOrEmpty(memo) && memo.Length == 6)
            return memo;

        // Try <AdditionalMemo>
        string? additionalMemo = doc.Descendants("AdditionalMemo")?.FirstOrDefault()?.Value;
        if (!string.IsNullOrEmpty(additionalMemo) && additionalMemo.Length == 6)
            return additionalMemo;

        // Try <LoyalityCustomer> Just in Case For Piccadilly
        string? loyalty = doc.Descendants("LoyalityCustomer")?.FirstOrDefault()?.Value;
        if (!string.IsNullOrEmpty(loyalty) && loyalty.Length == 6)
            return loyalty;

        return null;
    }

    private async Task HandleFileEvent(string filePath, CancellationToken stoppingToken)
    {
        string fileName = Path.GetFileName(filePath);
        if (fileName.ToLower().StartsWith("openchks"))
        {
            File.Delete(filePath);
            return;
        }

        for (int retry = 0; retry < MaxRetries; retry++)
        {
            try
            {
                var xmlContent = await File.ReadAllTextAsync(filePath, stoppingToken);
                var doc = XDocument.Parse(xmlContent);

                if (doc.Root != null)
                {
                    if (doc.Root.Name.LocalName == "CheckFinalization")
                    {
                        var memo = GetLoyalityMemoCode(doc);

                        if (memo?.Length == 6)
                        {
                            var xmlDoc = new XmlDocument();
                            xmlDoc.LoadXml(doc.ToString());
                            await _vendure.ReportInStoreSales(new Dictionary<string, string> { {
                                    "xml",
                                    Newtonsoft.Json.JsonConvert.SerializeXmlNode(xmlDoc)
                                } });

                            // Console.WriteLine($"{filePath} reported to /instore-sales-report");
                        }
                        
                        if (_s3Client != null && fileName.StartsWith("D"))
                        {
                            var key = GenerateS3Key(fileName);
                            await _s3Client.PutObjectAsync(new PutObjectRequest
                            { BucketName = _bucketName, Key = key, FilePath = filePath }
                            );

                            // Console.WriteLine($"{filePath} uploaded to {key} to process later.");
                        }
                    }

                    else if (doc.Root.Name.LocalName == "PrepOrder")
                    {
                        var storeNumber = doc.Root.Element("StoreNumber")?.Value;
                        var memos = doc.Descendants("Option").Elements("Memo").Select(m => m.Value).ToList();
                        var checkNumber = doc.Descendants("CheckHeader").Elements("CheckNumber").FirstOrDefault()?.Value;

                        var memo = memos.FirstOrDefault(m => !string.IsNullOrWhiteSpace(m));

                        if (memo?.Length == 6)
                        {
                            var payload = new Dictionary<string, object?>
                        {
                            { "StoreNumber", storeNumber },
                            { "CheckNumber", checkNumber },
                            { "LoyaltyMemo", memo }
                        };

                            await _vendure.ReportPrepOrder(JsonSerializer.Serialize(payload));

                            // Console.WriteLine($"{filePath} reported to /prep-sales-report");
                        }
                    }
                    File.Delete(filePath);
                    return;
                };
            }
            catch (IOException) when (retry < MaxRetries - 1)
            {
                // Handle file access exceptions (e.g., file in use)
                await Task.Delay(1000, stoppingToken);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing XML file {filePath}: {ex.Message}");
                return;
            }
        }
    }

    private FileSystemWatcher WatchForInStoreSales(CancellationToken stoppingToken)
    {
        var watcher = new FileSystemWatcher(watchFolderPath)
        {
            Filter = "*.xml",
            EnableRaisingEvents = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
        };

        watcher.Created += async (sender, e) => await HandleFileEvent(e.FullPath, stoppingToken);
        watcher.Renamed += async (sender, e) => await HandleFileEvent(e.FullPath, stoppingToken);
        return watcher;
    }
}
