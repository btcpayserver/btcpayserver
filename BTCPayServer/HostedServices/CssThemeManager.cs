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
using Microsoft.AspNetCore.Mvc.Filters;
using BTCPayServer.Security;

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

        internal void Update(PoliciesSettings data)
        {
            ShowRegister = !data.LockSubscription;
        }
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

    public class CssThemeManagerHostedService : BaseAsyncService
    {
        private SettingsRepository _SettingsRepository;
        private CssThemeManager _CssThemeManager;

        public CssThemeManagerHostedService(SettingsRepository settingsRepository, CssThemeManager cssThemeManager)
        {
            _SettingsRepository = settingsRepository;
            _CssThemeManager = cssThemeManager;
        }

        internal override Task[] InitializeTasks()
        {
            return new[]
            {
                CreateLoopTask(ListenForThemeChanges),
                CreateLoopTask(ListenForPoliciesChanges),
            };
        }

        async Task ListenForPoliciesChanges()
        {
            await new SynchronizationContextRemover();
            var data = (await _SettingsRepository.GetSettingAsync<PoliciesSettings>()) ?? new PoliciesSettings();
            _CssThemeManager.Update(data);
            await _SettingsRepository.WaitSettingsChanged<PoliciesSettings>(Cancellation);
        }

        async Task ListenForThemeChanges()
        {
            await new SynchronizationContextRemover();
            var data = (await _SettingsRepository.GetSettingAsync<ThemeSettings>()) ?? new ThemeSettings();
            _CssThemeManager.Update(data);

            await _SettingsRepository.WaitSettingsChanged<ThemeSettings>(Cancellation);
        }
    }
}
