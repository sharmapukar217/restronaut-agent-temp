using System;
using System.Threading;
using System.Threading.Tasks;
using Cronos;
using Microsoft.Extensions.Hosting;

namespace RestronautService
{
    public class OtaUpdaterService : BackgroundService
    {
        private Boolean _init = false;
        private readonly CronExpression _cron;
        private readonly TimeZoneInfo _timeZone;
        private readonly OtaUpdaterUtils _utils;
        public OtaUpdaterService(OtaUpdaterUtils utils)
        {
            _utils = utils;
            _timeZone = TimeZoneInfo.Local;
            _cron = CronExpression.Parse(_utils.cronPattern);
            _ = Task.Run(() => RunUpdater());
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var next = _cron.GetNextOccurrence(DateTimeOffset.Now, _timeZone);
                if (next == null) break;

                var delay = next.Value - DateTimeOffset.Now;

                try
                {
                    await Task.Delay(delay, stoppingToken);
                }
                catch (TaskCanceledException)
                {
                    break;
                }

                if (stoppingToken.IsCancellationRequested) break;
                if (!_utils.isUpgrading && !_init)
                {
                    _init = true;
                    await _utils.ApplyUpdate(false);
                }
            }
        }

        private async Task RunUpdater()
        {
            if (_utils.isUpgrading) return;

            var latestRelease = await _utils.GetLatestRelease();
            if (latestRelease != null && latestRelease.TagName != _utils.currentVersion)
            {
                _utils.Logger("A new version is available. Press (u) to update.");
            }
            else
            {
                Console.WriteLine("");
                _utils.Logger($"Using latest version {_utils.currentVersion}");
            }
        }
    }

}