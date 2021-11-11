using System.Collections.Generic;
using System.Threading.Tasks;
using BTCPayServer.Plugins.Test.Data;
using BTCPayServer.Plugins.Test.Services;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Plugins.Test
{
    [Route("extensions/test")]
    public class UITestExtensionController : Controller
    {
        private readonly TestPluginService _testPluginService;

        public UITestExtensionController(TestPluginService testPluginService)
        {
            _testPluginService = testPluginService;
        }

        // GET
        public async Task<IActionResult> Index()
        {
            return View(new TestPluginPageViewModel()
            {
                Data = await _testPluginService.Get()
            });
        }


    }

    public class TestPluginPageViewModel
    {
        public List<TestPluginData> Data { get; set; }
    }
}
