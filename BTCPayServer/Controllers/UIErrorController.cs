using System;
using System.Linq;
using BTCPayServer.Models;
using BTCPayServer.Security;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Controllers
{
    [IgnoreAntiforgeryToken]
    public class UIErrorController : Controller
    {
        public const string ErrorDetailsKey = "ERROR_DETAILS";
        public const string MissingPermissionQueryKey = "permission";

        [Route("/errors/{statusCode:int}")]
        public IActionResult Handle(int? statusCode = null)
        {
            if (Request.Headers.TryGetValue("Accept", out var v) && v.Any(o => o.Contains("text/html", StringComparison.OrdinalIgnoreCase)))
            {
                if (statusCode.HasValue)
                {
                    var specialPages = new[] { 404, 406, 417, 429, 500, 502, 403 };
                    if (specialPages.Any(a => a == statusCode.Value))
                    {
                        var viewName = statusCode.ToString();
                        if (statusCode.Value == 403)
                            return View(viewName, GetMissingPermission());
                        return View(viewName);
                    }
                }
                return View(statusCode);
            }
            return this.StatusCode(statusCode ?? 500);
        }

        private string GetMissingPermission()
        {
            string missingPermission = null;
            if (HttpContext.Items.TryGetValue(PermissionAuthorizationHandler.PolicyRequirementKey, out var requirement) &&
                requirement is PolicyRequirement policyRequirement)
            {
                missingPermission = policyRequirement.Policy;
            }
            else if (Request.Query.TryGetValue(MissingPermissionQueryKey, out var permission))
            {
                missingPermission = permission.FirstOrDefault();
            }
            return missingPermission;
        }
    }
}
