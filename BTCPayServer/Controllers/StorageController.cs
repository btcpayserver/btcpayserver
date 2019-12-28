using System.Threading.Tasks;
using BTCPayServer.Configuration;
using BTCPayServer.Storage.Services;
using BTCPayServer.Storage.Services.Providers.FileSystemStorage;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Storage
{
    [Route("Storage")]
    public class StorageController : Controller
    {
        private readonly FileService _FileService;

        public StorageController(FileService fileService, BTCPayServerOptions serverOptions)
        {
            _FileService = fileService;
            FileSystemFileProviderService.GetTempStorageDir(serverOptions);
        }

        [HttpGet("{fileId}")]
        public async Task<IActionResult> GetFile(string fileId)
        {
            var url = await _FileService.GetFileUrl(Request.GetAbsoluteRootUri(), fileId);
            return new RedirectResult(url);
        }
    }
}
