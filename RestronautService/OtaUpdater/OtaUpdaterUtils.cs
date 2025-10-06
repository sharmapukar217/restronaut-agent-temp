using System;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Linq;
using LazyCache;
using System.IO;
using System.Diagnostics;
using System.IO.Compression;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace RestronautService
{
    public class Release
    {
        public required string TagName { get; set; }
        public required string Body { get; set; }
        public required string DownloadUrl { get; set; }
        public required DateTime CreatedAt { get; set; }

    }

    public class OtaUpdaterUtils
    {
        private readonly string _otaUpdateUrl;
        private readonly string _otaUpdateKey;

        public bool isUpgrading = true;
        public readonly string cronPattern;
        public readonly string currentVersion = "v1.0.0";

        IAppCache cache = new CachingService();
        private readonly HttpClient _httpClient = new();

        public void Logger(string message)
        {
            string timestamp = DateTime.Now.ToString("dd/MM/yyyy hh:mm:ss tt");
            Console.WriteLine($"[{timestamp} ({currentVersion})]: {message}");
        }


        public OtaUpdaterUtils(string updateUrl, string updateKey, string? cron)
        {
            isUpgrading = false;
            _otaUpdateUrl = updateUrl;
            _otaUpdateKey = updateKey;
            cronPattern = cron ?? "0 2 * * *";

            var version = Assembly.GetEntryAssembly()?.GetName().Version;
            if (version != null)
            {
                currentVersion = $"v{version.Major}.{version.Minor}.{version.Build}";
            }

            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_otaUpdateKey}");
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd($"RestronautService/{currentVersion}");
        }
        public async Task<Release?> GetLatestRelease()
        {
            if (isUpgrading) return null;

            return await cache.GetOrAddAsync<Release?>("latest-release", async () =>
            {
                try
                {
                    isUpgrading = true;
                    Logger("Checking for latest updates...");
                    var response = await _httpClient.SendAsync(
                        new HttpRequestMessage(HttpMethod.Get, $"{_otaUpdateUrl}/latest")
                    );
                    response.EnsureSuccessStatusCode();

                    string responseBody = await response.Content.ReadAsStringAsync();
                    JsonElement root = JsonDocument.Parse(responseBody).RootElement;

                    if (!root.TryGetProperty("assets", out var assets)) return null;

                    return new Release
                    {
                        Body = root.GetProperty("body").GetString()!,
                        TagName = root.GetProperty("tag_name").GetString()!,
                        CreatedAt = root.GetProperty("created_at").GetDateTime(),
                        DownloadUrl = root.GetProperty("assets").EnumerateArray().FirstOrDefault().GetProperty("url").GetString()!
                    };
                }
                catch (Exception ex)
                {
                    Logger($"Couldn't fetch version info... {ex.Message}");
                    return null;
                }
                finally { isUpgrading = false; }
            });
        }


        private bool IsZipValid(string zipPath)
        {
            try
            {
                using (var zip = ZipFile.OpenRead(zipPath))
                {
                    foreach (var entry in zip.Entries) _ = entry.FullName;
                }
                return true;
            }
            catch
            {
                return false;
            }
        }


        public async Task ApplyUpdate(bool shouldShowPrompt = true)
        {
            await Task.Run(() => _ApplyUpdate(shouldShowPrompt));
        }


        private async Task _ApplyUpdate(bool shouldShowPrompt = true)
        {
            try
            {
                var latestRelease = await GetLatestRelease();
                isUpgrading = true;

                if (latestRelease == null || latestRelease.TagName.Trim() == currentVersion.Trim())
                {
                    if (shouldShowPrompt)
                    {
                        Logger($"Already on latest version {currentVersion}");
                    }
                    isUpgrading = false;
                    return;
                }

                Logger($"Current version: {currentVersion}");
                Logger($"Latest version : {latestRelease.TagName} - {latestRelease.Body}");

                var request = new HttpRequestMessage(HttpMethod.Get, latestRelease.DownloadUrl);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));

                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                var canReportProgress = totalBytes != -1;

                // Create temp folder
                string tempFolder = Path.Combine(Path.GetTempPath(), "RestronautUpdate");
                Directory.CreateDirectory(tempFolder);

                string tempZipPath = Path.Combine(tempFolder, $"release_{latestRelease.TagName}.zip");

                if (!File.Exists(tempZipPath) || !IsZipValid(tempZipPath))
                {
                    using var input = await response.Content.ReadAsStreamAsync();
                    using var output = new FileStream(tempZipPath, FileMode.Create, FileAccess.Write, FileShare.None);

                    long totalRead = 0;
                    var buffer = new byte[8192];
                    int read, lastPercent = -1, barWidth = 30;

                    while ((read = await input.ReadAsync(buffer)) > 0)
                    {
                        await output.WriteAsync(buffer.AsMemory(0, read));
                        totalRead += read;

                        if (canReportProgress)
                        {
                            var percent = (int)((totalRead * 100) / totalBytes);
                            if (percent != lastPercent)
                            {
                                int filledBars = (percent * barWidth) / 100;
                                int emptyBars = barWidth - filledBars;
                                lastPercent = percent;

                                string bar = new string('█', filledBars) + new string('░', emptyBars);
                                Console.Write($"\rDownloading update... {bar} {percent,3}%");
                            }
                        }
                    }

                    Console.WriteLine("");
                    Logger("Download completed. ");
                }
                if (shouldShowPrompt)
                {
                    Logger("Application needs to be stopped to apply update. Continue [Y/n]: ");

                    if (char.ToLowerInvariant(Console.ReadKey(true).KeyChar) == 'n')
                    {
                        Logger("Update aborted!");
                        isUpgrading = false;
                        return;
                    }
                }

                await Task.Delay(10);

                var extractPath = Path.Join(tempFolder, "extracted");
                Directory.CreateDirectory(extractPath);

                using var archive = ArchiveFactory.Open(tempZipPath);
                foreach (var entry in archive.Entries.Where(e => !e.IsDirectory))
                {
                    entry.WriteToDirectory(extractPath, new ExtractionOptions
                    {
                        ExtractFullPath = true,
                        Overwrite = true
                    });
                }

                using var stream = Assembly.GetExecutingAssembly()
                    .GetManifestResourceStream("RestronautService.OtaUpdater.updater.bat");

                if (stream == null)
                {
                    Logger("Couldn't apply update. Please try again later.");
                    return;
                }

                using var reader = new StreamReader(stream);
                string batContent = reader.ReadToEnd().Replace("{{BASE_DIR}}", tempFolder).Replace("{{VERSION}}", currentVersion);

                //// Write the batch file content to the temp folder
                string tempBatPath = Path.Combine(tempFolder, "update.bat");
                await File.WriteAllTextAsync(tempBatPath, batContent);

                var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                Process.Start(tempBatPath, $"\"{exePath}\"");
            }
            catch (Exception ex)
            {
                Logger($"Update failed: {ex.Message}");
                isUpgrading = false;
            }
            finally
            {
                isUpgrading = false;
            }
        }
    }
}
