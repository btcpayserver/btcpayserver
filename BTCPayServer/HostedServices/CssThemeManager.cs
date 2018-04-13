using System;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Logging;
using Microsoft.Extensions.Hosting;
using NBXplorer;
using NBXplorer.Models;
using System.Collections.Concurrent;
using BTCPayServer.Events;
using BTCPayServer.Services;

namespace BTCPayServer.HostedServices
{
    public class CssThemeManager
    {
        public CssThemeManager(SettingsRepository settingsRepository)
        {
            Update(settingsRepository);
        }

        private string _bootstrapThemeUri;
        public string BootstrapThemeUri
        {
            get { return _bootstrapThemeUri; }
        }

        private async void Update(SettingsRepository settingsRepository)
        {
            var data = (await settingsRepository.GetSettingAsync<PoliciesSettings>()) ?? new PoliciesSettings();
            Update(data.CustomBootstrapThemeCssUri);
        }

        public void Update(string newUri)
        {
            if (String.IsNullOrWhiteSpace(newUri))
                _bootstrapThemeUri = "/vendor/bootstrap4/css/bootstrap.css";
            else
                _bootstrapThemeUri = newUri;
        }
    }
}
