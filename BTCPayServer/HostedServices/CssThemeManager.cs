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

        private async void Update(SettingsRepository settingsRepository)
        {
            var data = (await settingsRepository.GetSettingAsync<PoliciesSettings>()) ?? new PoliciesSettings();
            Update(data);
        }

        public void Update(PoliciesSettings data)
        {
            UpdateBootstrap(data.BootstrapCssUri);
            UpdateCreativeStart(data.CreativeStartCssUri);
        } 

        private string _bootstrapUri;
        public string BootstrapUri
        {
            get { return _bootstrapUri; }
        }
        public void UpdateBootstrap(string newUri)
        {
            if (String.IsNullOrWhiteSpace(newUri))
                _bootstrapUri = "/vendor/bootstrap4/css/bootstrap.css";
            else
                _bootstrapUri = newUri;
        }

        private string _creativeStartUri;
        public string CreativeStartUri
        {
            get { return _creativeStartUri; }
        }
        public void UpdateCreativeStart(string newUri)
        {
            if (String.IsNullOrWhiteSpace(newUri))
                _creativeStartUri = "/vendor/bootstrap4-creativestart/creative.css";
            else
                _creativeStartUri = newUri;
        }
    }
}
