using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Controllers
{
    [Route("[controller]/[action]")]
    public class ErrorController : Controller
    {
        public IActionResult Handle(int? statusCode = null)
        {
            if (statusCode.HasValue)
            {
                var specialPages = new[] { 404, 429, 500 };
                if (specialPages.Any(a => a == statusCode.Value))
                {
                    var viewName = statusCode.ToString();
                    return View(viewName);
                }
            }
            return View(statusCode);
        }
    }
}
