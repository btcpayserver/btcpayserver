using System;
using System.IO;
using System.Threading.Tasks;
using BTCPayServer.Configuration;
using BTCPayServer.Storage.Services;
using BTCPayServer.Storage.Services.Providers.FileSystemStorage;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;

namespace BTCPayServer.Storage
{
    [Route("Storage")]
    public class StorageController : Controller
    {
        private readonly FileService _FileService;
        private string _dir;

        public StorageController(FileService fileService, BTCPayServerOptions serverOptions)
        {
            _FileService = fileService;
            _dir =FileSystemFileProviderService.GetTempStorageDir(serverOptions);
        }

        [HttpGet("{fileId}")]
        public async Task<IActionResult> GetFile(string fileId)
        {
            var url = await _FileService.GetFileUrl(fileId);
            return new RedirectResult(url);
        }

        [HttpGet("LocalStorage/tmp/{tmpfileId}")]
        public async Task<IActionResult> GetLocalStorageTemporaryFile(string tmpfileId, bool download)
        {
            var path = Path.Combine(_dir, tmpfileId);
            if (!System.IO.File.Exists(path))
            {
                return NotFound();
            }

            var text = await System.IO.File.ReadAllTextAsync(path);
            var descriptor = JsonConvert.DeserializeObject<TemporaryLocalFileDescriptor>(text);
            if (descriptor.Expiry > DateTime.Now)
            {
                return NotFound();
            }

            var url = await _FileService.GetFileUrl(descriptor.FileId);
            return new RedirectResult(url);
        }

        public class TemporaryLocalFileDescriptor
        {
            public string FileId { get; set; }
            public bool IsDownload { get; set; }
            public DateTimeOffset Expiry { get; set; }
        }
    }
}
