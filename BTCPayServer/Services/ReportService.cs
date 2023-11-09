using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Services.Reporting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BTCPayServer.Services
{
    public class ReportService : IHostedService
    {
        private readonly SettingsRepository _settingsRepository;
        private readonly IServiceProvider _serviceProvider;

        public ReportService(IEnumerable<ReportProvider> reportProviders, SettingsRepository settingsRepository,
            IServiceProvider serviceProvider)
        {
            _settingsRepository = settingsRepository;
    
            _serviceProvider = serviceProvider;
            foreach (var r in reportProviders)
            {
                ReportProviders.Add(r.Name, r);
            }
        }

        public Dictionary<string, ReportProvider> ReportProviders { get; } = new();

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            var result = await _settingsRepository.GetSettingAsync<DynamicReportsSettings>();
            if (result?.Reports?.Any() is true)
            {
                foreach (var report in result.Reports)
                {
                    var reportProvider = ActivatorUtilities.CreateInstance<PostgresReportProvider>(_serviceProvider);
                    reportProvider.Setting = report.Value;
                    reportProvider.ReportName = report.Key;
                    ReportProviders.TryAdd(report.Key, reportProvider);
                }
            }
        }

        public async Task UpdateDynamicReport(string name, DynamicReportsSettings.DynamicReportSetting setting)
        {
            ReportProviders.TryGetValue(name, out var report);
            if (report is not null && report is not PostgresReportProvider)
            {
                throw new InvalidOperationException("Only PostgresReportProvider can be updated dynamically");
            }

            var result = await _settingsRepository.GetSettingAsync<DynamicReportsSettings>() ?? new DynamicReportsSettings();
            if (report is PostgresReportProvider postgresReportProvider)
            {
                if (setting is null)
                {
                    //remove report
                    ReportProviders.Remove(name);

                    result.Reports.Remove(name);
                    await _settingsRepository.UpdateSetting(result);
                }
                else
                {
                    postgresReportProvider.Setting = setting;
                    result.Reports[name] = setting;
                    postgresReportProvider.ReportName = name;
                    await _settingsRepository.UpdateSetting(result);
                }
            }
            else if (setting is not null)
            {
                var reportProvider = ActivatorUtilities.CreateInstance<PostgresReportProvider>(_serviceProvider);
                reportProvider.Setting = setting;

                reportProvider.ReportName = name;
                result.Reports[name] = setting;
                await _settingsRepository.UpdateSetting(result);
                ReportProviders.TryAdd(name, reportProvider);
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
        }
    }

    public class DynamicReportsSettings
    {
        public Dictionary<string, DynamicReportSetting> Reports { get; set; } = new();

        public class DynamicReportSetting
        {
            public string Sql { get; set; }
            public bool AllowForNonAdmins { get; set; }
        }
    }
}
