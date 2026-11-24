using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Models.ServerViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Controllers
{
    public partial class UIServerController
    {
        [HttpGet("server/files")]
        public async Task<IActionResult> Files([FromQuery] string[] fileIds = null)
        {
            var model = new ViewFilesViewModel()
            {
                Files = await _StoredFileRepository.GetFiles(),
                DirectUrlByFiles = null
            };

            if (fileIds != null && fileIds.Length > 0)
            {
                bool allFilesExist = true;
                Dictionary<string, string> directUrlByFiles = new Dictionary<string, string>();
                foreach (string filename in fileIds)
                {
                    string fileUrl = await _fileService.GetFileUrl(Request.GetAbsoluteRootUri(), filename);
                    if (fileUrl == null)
                    {
                        allFilesExist = false;
                        break;
                    }
                    directUrlByFiles.Add(filename, fileUrl);
                }

                if (!allFilesExist)
                {
                    TempData.SetStatusMessageModel(new StatusMessageModel
                    {
                        Message = StringLocalizer["Some of the files were not found"].Value,
                        Severity = StatusMessageModel.StatusSeverity.Warning,
                    });
                }
                else
                {
                    model.DirectUrlByFiles = directUrlByFiles;
                }
            }
            return View(model);
        }

        [HttpPost("server/files/{fileId}/delete")]
        public async Task<IActionResult> DeleteFile(string fileId)
        {
            try
            {
                await _fileService.RemoveFile(fileId, null);
                return RedirectToAction(nameof(Files), new
                {
                    fileIds = Array.Empty<string>(),
                    statusMessage = StringLocalizer["File removed"].Value
                });
            }
            catch (Exception e)
            {
                TempData.SetStatusMessageModel(new StatusMessageModel
                {
                    Severity = StatusMessageModel.StatusSeverity.Error,
                    Message = e.Message
                });
                return RedirectToAction(nameof(Files));
            }
        }

        [HttpPost("server/files/upload")]
        public async Task<IActionResult> CreateFiles(List<IFormFile> files)
        {
            if (files != null && files.Count > 0)
            {
                int invalidFileNameCount = 0;
                List<string> fileIds = new List<string>();
                foreach (IFormFile file in files)
                {
                    if (!file.FileName.IsValidFileName())
                    {
                        invalidFileNameCount++;
                        continue;
                    }
                    var newFile = await _fileService.AddFile(file, GetUserId());
                    fileIds.Add(newFile.Id);
                }

                StatusMessageModel.StatusSeverity statusMessageSeverity;
                string statusMessage;

                if (invalidFileNameCount == 0)
                {
                    statusMessage = StringLocalizer["Files added successfully"];
                    statusMessageSeverity = StatusMessageModel.StatusSeverity.Success;
                }
                else if (invalidFileNameCount > 0 && invalidFileNameCount < files.Count)
                {
                    statusMessage = StringLocalizer["{0} files were added. {1} files had invalid names", files.Count - invalidFileNameCount, invalidFileNameCount].Value;
                    statusMessageSeverity = StatusMessageModel.StatusSeverity.Error;
                }
                else
                {
                    statusMessage = StringLocalizer["Files could not be added due to invalid names"].Value;
                    statusMessageSeverity = StatusMessageModel.StatusSeverity.Error;
                }

                TempData.SetStatusMessageModel(new StatusMessageModel
                {
                    Message = statusMessage,
                    Severity = statusMessageSeverity
                });

                return RedirectToAction(nameof(Files), new
                {
                    fileIds = fileIds.ToArray(),
                });
            }
            else
            {
                return RedirectToAction(nameof(Files));
            }
        }

        private string GetUserId() => ControllerContext.HttpContext.User.GetIdOrNull();

    }
}
