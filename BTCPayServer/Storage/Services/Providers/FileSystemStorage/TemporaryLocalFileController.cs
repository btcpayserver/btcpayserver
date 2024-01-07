using System;
using System.IO;
using System.Net.Mime;
using System.Threading.Tasks;
using BTCPayServer.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace BTCPayServer.Storage.Services.Providers.FileSystemStorage;

public class TemporaryLocalFileController : Controller
{
    private readonly StoredFileRepository _storedFileRepository;
    private readonly IOptions<DataDirectories> _dataDirectories;

    public TemporaryLocalFileController(StoredFileRepository storedFileRepository,
        IOptions<DataDirectories> dataDirectories)
    {
        _storedFileRepository = storedFileRepository;
        _dataDirectories = dataDirectories;
    }

    [HttpGet($"~/{FileSystemFileProviderService.LocalStorageDirectoryName}tmp/{{tmpFileId}}")]
    public async Task<IActionResult> GetTmpLocalFile(string tmpFileId)
    {
        var path = Path.Combine(_dataDirectories.Value.TempStorageDir, tmpFileId);

        if (!System.IO.File.Exists(path))
        {
            return NotFound();
        }

        var text = await System.IO.File.ReadAllTextAsync(path);
        var descriptor = JsonConvert.DeserializeObject<TemporaryLocalFileDescriptor>(text);
        if (descriptor.Expiry < DateTime.UtcNow)
        {
            System.IO.File.Delete(path);
            return NotFound();
        }

        var storedFile = _storedFileRepository.GetFile(descriptor.FileId).GetAwaiter().GetResult();

        ControllerContext.HttpContext.Response.Headers["Content-Disposition"] =
            ControllerContext.HttpContext.Request.Query.ContainsKey("download") ? "attachment" : "inline";
        ControllerContext.HttpContext.Response.Headers["Content-Security-Policy"] = "script-src ;";
        ControllerContext.HttpContext.Response.Headers["X-Content-Type-Options"] = "nosniff";
        path = Path.Combine(_dataDirectories.Value.StorageDir, storedFile.StorageFileName);
        var fileContent = await System.IO.File.ReadAllBytesAsync(path);
        return File(fileContent, MediaTypeNames.Application.Octet, storedFile.FileName);
    }
}
