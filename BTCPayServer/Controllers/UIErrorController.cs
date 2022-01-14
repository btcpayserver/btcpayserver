using System;
using System.Linq;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Controllers
{
    public class UIErrorController : Controller
    {
        [Route("/errors/{statusCode:int}")]
        public IActionResult Handle(int? statusCode = null)
        {
            if (Request.Headers.TryGetValue("Accept", out var v) && v.Any(v => v.Contains("text/html", StringComparison.OrdinalIgnoreCase)))
            {
                if (statusCode.HasValue)
                {
                    var specialPages = new[] { 404, 406, 417, 429, 500, 502, 403 };
                    if (specialPages.Any(a => a == statusCode.Value))
                    {
                        var viewName = statusCode.ToString();
                        return View(viewName);
                    }
                }
                return View(statusCode);
            }
            return this.StatusCode(statusCode.Value);
        }
    }
}
