using System.Threading.Tasks;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Storage.Services;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Storage
{
    [Route("Storage")]
    public class UIStorageController : Controller
    {
        private readonly FileService _FileService;

        public UIStorageController(FileService fileService)
        {
            _FileService = fileService;
        }

        [HttpGet("{fileId}")]
        public async Task<IActionResult> GetFile(string fileId)
        {
            var url = await _FileService.GetFileUrl(Request.GetAbsoluteRootUri(), fileId);
            if (url is null)
                return NotFound();
            return new RedirectResult(url);
        }
    }
}
