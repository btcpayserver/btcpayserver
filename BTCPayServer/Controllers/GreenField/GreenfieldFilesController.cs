using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Storage.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Controllers.Greenfield;

[ApiController]
[EnableCors(CorsPolicies.All)]
[Authorize(Policy = Policies.CanModifyServerSettings, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
public class GreenfieldFilesController(
    UserManager<ApplicationUser> userManager,
    IFileService fileService,
    StoredFileRepository fileRepository)
    : Controller
{
    [HttpGet("~/api/v1/files")]
    public async Task<IActionResult> GetFiles()
    {
        var storedFiles = await fileRepository.GetFiles();
        var files = new List<FileData>();
        foreach (var file in storedFiles)
            files.Add(await ToFileData(file));
        return Ok(files);
    }

    [HttpGet("~/api/v1/files/{fileId}")]
    public async Task<IActionResult> GetFile(string fileId)
    {
        var file = await fileRepository.GetFile(fileId);
        return file == null
            ? this.CreateAPIError(404, "file-not-found", "The file does not exist.")
            : Ok(await ToFileData(file));
    }

    [HttpPost("~/api/v1/files")]
    public async Task<IActionResult> UploadFile(IFormFile file)
    {
        if (file is null)
            ModelState.AddModelError(nameof(file), "Invalid file");
        else if (!file.FileName.IsValidFileName())
            ModelState.AddModelError(nameof(file.FileName), "Invalid filename");
        if (!ModelState.IsValid)
            return this.CreateValidationError(ModelState);

        try
        {
            var userId = userManager.GetUserId(User)!;
            var newFile = await fileService.AddFile(file!, userId);
            return Ok(await ToFileData(newFile));
        }
        catch (Exception e)
        {
            return this.CreateAPIError(404, "file-upload-failed", e.Message);
        }
    }

    [HttpDelete("~/api/v1/files/{fileId}")]
    public async Task<IActionResult> DeleteFile(string fileId)
    {
        var file = await fileRepository.GetFile(fileId);
        if (file == null) return this.CreateAPIError(404, "file-not-found", "The file does not exist.");
        await fileRepository.RemoveFile(file);
        return Ok();
    }

    private async Task<FileData> ToFileData(IStoredFile file)
    {
        return new FileData
        {
            Id = file.Id,
            UserId = file.ApplicationUserId,
            Uri = new UnresolvedUri.FileIdUri(file.Id).ToString(),
            Url = await fileService.GetFileUrl(Request.GetAbsoluteRootUri(), file.Id),
            OriginalName = file.FileName,
            StorageName = file.StorageFileName,
            CreatedAt = file.Timestamp
        };
    }
}
