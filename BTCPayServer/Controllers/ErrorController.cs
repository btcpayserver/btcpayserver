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
                if (statusCode.Value == 404)
                {
                    var viewName = statusCode.ToString();
                    return View(viewName);
                }
            }
            return View(statusCode);
        }
    }
}
