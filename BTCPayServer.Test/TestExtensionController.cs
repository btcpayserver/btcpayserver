using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Test
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
