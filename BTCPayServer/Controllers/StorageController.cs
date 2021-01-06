using System.Threading.Tasks;
using BTCPayServer.Storage.Services;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Storage
{
    [Route("Storage")]
    public class StorageController : Controller
    {
        private readonly FileService _FileService;

        public StorageController(FileService fileService)
        {
            _FileService = fileService;
        }

        [HttpGet("{fileId}")]
        public async Task<IActionResult> GetFile(string fileId)
        {
            var url = await _FileService.GetFileUrl(Request.GetAbsoluteRootUri(), fileId);
            return new RedirectResult(url);
        }
    }
}
