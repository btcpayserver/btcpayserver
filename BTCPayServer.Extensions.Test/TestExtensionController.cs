using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Extensions.Test
{
    [Route("extensions/test")]
    public class TestExtensionController : Controller
    {
        // GET
        public IActionResult Index()
        {
            return View();
        }
        
        
    }
}
