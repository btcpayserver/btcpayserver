using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using NBXplorer;
using BTCPayServer.Events;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Mvc.Filters;
using BTCPayServer.Security;
using BTCPayServer.Services.Apps;

namespace BTCPayServer.HostedServices
{
    public class CssThemeManager
    {
        public void Update(ThemeSettings data)
        {
            if (String.IsNullOrWhiteSpace(data.BootstrapCssUri))
                _bootstrapUri = "/vendor/bootstrap4/css/bootstrap.css?v=" + DateTime.Now.Ticks;
            else
                _bootstrapUri = data.BootstrapCssUri;


            if (String.IsNullOrWhiteSpace(data.CreativeStartCssUri))
                _creativeStartUri = "/vendor/bootstrap4-creativestart/creative.css?v=" + DateTime.Now.Ticks;
            else
                _creativeStartUri = data.CreativeStartCssUri;
        }

        private string _bootstrapUri;
        public string BootstrapUri
        {
            get { return _bootstrapUri; }
        }

        private string _creativeStartUri;
        public string CreativeStartUri
        {
            get { return _creativeStartUri; }
        }


        public bool ShowRegister { get; set; }
        public bool DiscourageSearchEngines { get; set; }

        public AppType? RootAppType { get; set; }
        public string RootAppId { get; set; }

        public List<PoliciesSettings.DomainToAppMappingItem> DomainToAppMapping { get; set; }

        internal void Update(PoliciesSettings data)
        {
            ShowRegister = !data.LockSubscription;
            DiscourageSearchEngines = data.DiscourageSearchEngines;

            RootAppType = data.RootAppType;
            RootAppId = data.RootAppId;
            DomainToAppMapping = data.DomainToAppMapping;
            AllowLightningInternalNodeForAll = data.AllowLightningInternalNodeForAll;
        }

        public bool AllowLightningInternalNodeForAll { get; set; }
    }

    public class ContentSecurityPolicyCssThemeManager : Attribute, IActionFilter, IOrderedFilter
    {
        public int Order => 1001;

        public void OnActionExecuted(ActionExecutedContext context)
        {
            
        }

        public void OnActionExecuting(ActionExecutingContext context)
        {
            var manager = context.HttpContext.RequestServices.GetService(typeof(CssThemeManager)) as CssThemeManager;
            var policies = context.HttpContext.RequestServices.GetService(typeof(ContentSecurityPolicies)) as ContentSecurityPolicies;
            if (manager != null && policies != null)
            {
                if(manager.CreativeStartUri != null && Uri.TryCreate(manager.CreativeStartUri, UriKind.Absolute, out var uri))
                {
                    policies.Clear();
                }
                if (manager.BootstrapUri != null && Uri.TryCreate(manager.BootstrapUri, UriKind.Absolute, out uri))
                {
                    policies.Clear();
                }
            }
        }
    }

    public class CssThemeManagerHostedService : IHostedService
    {
        private readonly EventAggregator _EventAggregator;
        private readonly SettingsRepository _SettingsRepository;
        private readonly CssThemeManager _CssThemeManager;

        private CompositeDisposable leases = new CompositeDisposable();
        public CssThemeManagerHostedService(SettingsRepository settingsRepository, CssThemeManager cssThemeManager, EventAggregator eventAggregator)
        {
            _EventAggregator = eventAggregator;
            _SettingsRepository = settingsRepository;
            _CssThemeManager = cssThemeManager;
        }

        private async Task ListenForPoliciesChanges()
        {
            var data = (await _SettingsRepository.GetSettingAsync<PoliciesSettings>()) ?? new PoliciesSettings();
            _CssThemeManager.Update(data);
            leases.Add(_EventAggregator.Subscribe<SettingsChanged<PoliciesSettings>>(changed =>
            {
                _CssThemeManager.Update(changed.Settings);
            }));
        }

        async Task ListenForThemeChanges()
        {
            var data = (await _SettingsRepository.GetSettingAsync<ThemeSettings>()) ?? new ThemeSettings();
            _CssThemeManager.Update(data);
            leases.Add(_EventAggregator.Subscribe<SettingsChanged<ThemeSettings>>(changed =>
            {
                _CssThemeManager.Update(changed.Settings);
            }));
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            return Task.WhenAll(new[] {ListenForPoliciesChanges(), ListenForThemeChanges()});
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            leases.Dispose();
            return Task.CompletedTask;
        }
    }
}
