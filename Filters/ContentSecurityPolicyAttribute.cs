using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Security;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BTCPayServer.Filters
{
    public interface IContentSecurityPolicy : IFilterMetadata { }
    public enum CSPTemplate
    {
        AntiXSS
    }
    public class ContentSecurityPolicyAttribute : Attribute, IActionFilter, IContentSecurityPolicy
    {
        public ContentSecurityPolicyAttribute()
        {

        }
        public ContentSecurityPolicyAttribute(CSPTemplate template)
        {
            if (template == CSPTemplate.AntiXSS)
            {
                AutoSelf = false;
                FixWebsocket = false;
                UnsafeInline = false;
                ScriptSrc = "'self'";
            }
        }

        public void OnActionExecuted(ActionExecutedContext context)
        {

        }
        public bool Enabled { get; set; } = true;
        public bool AutoSelf { get; set; } = true;
        public bool UnsafeInline { get; set; } = true;
        public bool FixWebsocket { get; set; } = true;
        public string FontSrc { get; set; } = null;
        public string ImgSrc { get; set; } = null;
        public string DefaultSrc { get; set; }
        public string StyleSrc { get; set; }
        public string ScriptSrc { get; set; }
        public string ManifestSrc { get; set; }

        public void OnActionExecuting(ActionExecutingContext context)
        {
            if (!context.IsEffectivePolicy<IContentSecurityPolicy>(this) || !Enabled)
                return;
            var policies = context.HttpContext.RequestServices.GetService(typeof(ContentSecurityPolicies)) as ContentSecurityPolicies;
            if (policies == null)
                return;
            if (DefaultSrc != null)
            {
                policies.Add(new ConsentSecurityPolicy("default-src", DefaultSrc));
            }
            if (UnsafeInline)
            {
                policies.Add(new ConsentSecurityPolicy("script-src", "'unsafe-inline'"));
            }
            if (!string.IsNullOrEmpty(FontSrc))
            {
                policies.Add(new ConsentSecurityPolicy("font-src", FontSrc));
            }
            if (!string.IsNullOrEmpty(ManifestSrc))
            {
                policies.Add(new ConsentSecurityPolicy("manifest-src", FontSrc));
            }

            if (!string.IsNullOrEmpty(ImgSrc))
            {
                policies.Add(new ConsentSecurityPolicy("img-src", ImgSrc));
            }

            if (!string.IsNullOrEmpty(StyleSrc))
            {
                policies.Add(new ConsentSecurityPolicy("style-src", StyleSrc));
            }

            if (!string.IsNullOrEmpty(ScriptSrc))
            {
                policies.Add(new ConsentSecurityPolicy("script-src", ScriptSrc));
            }

            if (FixWebsocket && AutoSelf) // Self does not match wss:// and ws:// :(
            {
                var request = context.HttpContext.Request;

                var url = string.Concat(
                        request.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) ? "ws" : "wss",
                        "://",
                        request.Host.ToUriComponent(),
                        request.PathBase.ToUriComponent());
                policies.Add(new ConsentSecurityPolicy("connect-src", url));
            }

            context.HttpContext.Response.OnStarting(() =>
            {
                if (!policies.HasRules)
                    return Task.CompletedTask;
                if (AutoSelf)
                {
                    bool hasSelf = false;
                    foreach (var group in policies.Rules.GroupBy(p => p.Name))
                    {
                        hasSelf = group.Any(g => g.Value.Contains("'self'", StringComparison.OrdinalIgnoreCase));
                        if (!hasSelf && !group.Any(g => g.Value.Contains("'none'", StringComparison.OrdinalIgnoreCase) ||
                                           g.Value.Contains('*', StringComparison.OrdinalIgnoreCase)))
                        {
                            policies.Add(new ConsentSecurityPolicy(group.Key, "'self'"));
                        }
                    }
                }
                context.HttpContext.Response.SetHeader("Content-Security-Policy", policies.ToString());
                return Task.CompletedTask;
            });
        }
    }
}
