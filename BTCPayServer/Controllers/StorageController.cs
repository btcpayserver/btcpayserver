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
        private readonly string _dir;

        public StorageController(FileService fileService, DataDirectories datadirs)
        {
            _FileService = fileService;
            _dir = datadirs.TempStorageDir;
        }

        [HttpGet("{fileId}")]
        public async Task<IActionResult> GetFile(string fileId)
        {
            var url = await _FileService.GetFileUrl(Request.GetAbsoluteRootUri(), fileId);
            return new RedirectResult(url);
        }
    }
}
