using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Models;
using BTCPayServer.Models.ServerViewModels;
using BTCPayServer.Storage.Models;
using BTCPayServer.Storage.Services.Providers.AmazonS3Storage;
using BTCPayServer.Storage.Services.Providers.AmazonS3Storage.Configuration;
using BTCPayServer.Storage.Services.Providers.AzureBlobStorage;
using BTCPayServer.Storage.Services.Providers.AzureBlobStorage.Configuration;
using BTCPayServer.Storage.Services.Providers.FileSystemStorage;
using BTCPayServer.Storage.Services.Providers.FileSystemStorage.Configuration;
using BTCPayServer.Storage.Services.Providers.GoogleCloudStorage;
using BTCPayServer.Storage.Services.Providers.GoogleCloudStorage.Configuration;
using BTCPayServer.Storage.Services.Providers.Models;
using BTCPayServer.Storage.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Controllers
{
    public partial class ServerController
    {
        [HttpGet("server/files/{fileId?}")]
        public async Task<IActionResult> Files(string fileId = null, string statusMessage = null)
        {
            TempData["StatusMessage"] = statusMessage;
            var fileUrl = string.IsNullOrEmpty(fileId) ? null : await _FileService.GetFileUrl(fileId);

            return View(new ViewFilesViewModel()
            {
                Files = await _StoredFileRepository.GetFiles(),
                SelectedFileId = string.IsNullOrEmpty(fileUrl) ? null : fileId,
                DirectFileUrl = fileUrl,
                StorageConfigured = (await _SettingsRepository.GetSettingAsync<StorageSettings>()) != null
            });
        }

        [HttpGet("server/files/{fileId}/delete")]
        public async Task<IActionResult> DeleteFile(string fileId)
        {
            try
            {
                await _FileService.RemoveFile(fileId, null);
                return RedirectToAction(nameof(Files), new
                {
                    fileId = "",
                    statusMessage = "File removed"
                });
            }
            catch (Exception e)
            {
                return RedirectToAction(nameof(Files), new
                {
                    statusMessage = new StatusMessageModel()
                    {
                        Severity = StatusMessageModel.StatusSeverity.Error,
                        Message = e.Message
                    }
                });
            }
        }

        [HttpGet("server/files/{fileId}/tmp")]
        public async Task<IActionResult> CreateTemporaryFileUrl(string fileId)
        {
            var file = await _StoredFileRepository.GetFile(fileId);

            if (file == null)
            {
                return NotFound();
            }

            return View(new CreateTemporaryFileUrlViewModel());
        }

        [HttpPost("server/files/{fileId}/tmp")]
        public async Task<IActionResult> CreateTemporaryFileUrl(string fileId,
            CreateTemporaryFileUrlViewModel viewModel)
        {
            if (viewModel.TimeAmount <= 0)
            {
                ModelState.AddModelError(nameof(viewModel.TimeAmount), "Time must be at least 1");
            }
            
            if (!ModelState.IsValid)
            {
                return View(viewModel);
            }

            var file = await _StoredFileRepository.GetFile(fileId);

            if (file == null)
            {
                return NotFound();
            }

            var expiry = DateTimeOffset.Now;
            switch (viewModel.TimeType)
            {
                case CreateTemporaryFileUrlViewModel.TmpFileTimeType.Seconds:
                    expiry =expiry.AddSeconds(viewModel.TimeAmount);
                    break;
                case CreateTemporaryFileUrlViewModel.TmpFileTimeType.Minutes:
                    expiry = expiry.AddMinutes(viewModel.TimeAmount);
                    break;
                case CreateTemporaryFileUrlViewModel.TmpFileTimeType.Hours:
                    expiry = expiry.AddHours(viewModel.TimeAmount);
                    break;
                case CreateTemporaryFileUrlViewModel.TmpFileTimeType.Days:
                    expiry = expiry.AddDays(viewModel.TimeAmount);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            var url = await _FileService.GetTemporaryFileUrl(fileId, expiry, viewModel.IsDownload);

            return RedirectToAction(nameof(Files), new
            {
                StatusMessage = new StatusMessageModel()
                {
                    Html =
                        $"Generated Temporary Url for file {file.FileName} which expires at {expiry:G}. <a href='{url}' target='_blank'>{url}</a>"
                }.ToString(),
                fileId,
            });

        }

        public class CreateTemporaryFileUrlViewModel
        {
            public enum TmpFileTimeType
            {
                Seconds,
                Minutes,
                Hours,
                Days
            }
            public int TimeAmount { get; set; }
            public TmpFileTimeType TimeType { get; set; }
            public bool IsDownload { get; set; }
        }


        [HttpPost("server/files/upload")]
        public async Task<IActionResult> CreateFile(IFormFile file)
        {
            var newFile = await _FileService.AddFile(file, GetUserId());
            return RedirectToAction(nameof(Files), new
            {
                statusMessage = "File added!",
                fileId = newFile.Id
            });
        }

        private string GetUserId()
        {
            return _UserManager.GetUserId(ControllerContext.HttpContext.User);
        }

        [HttpGet("server/storage")]
        public async Task<IActionResult> Storage(bool forceChoice = false, string statusMessage = null)
        {
            TempData["StatusMessage"] = statusMessage;
            var savedSettings = await _SettingsRepository.GetSettingAsync<StorageSettings>();
            if (forceChoice || savedSettings == null)
            {
                return View(new ChooseStorageViewModel()
                {
                    ShowChangeWarning = savedSettings != null,
                    Provider = savedSettings?.Provider ?? BTCPayServer.Storage.Models.StorageProvider.FileSystem
                });
            }

            return RedirectToAction(nameof(StorageProvider), new
            {
                provider = savedSettings.Provider
            });
        }

        [HttpPost("server/storage")]
        public IActionResult Storage(StorageSettings viewModel)
        {
            return RedirectToAction("StorageProvider", "Server", new
            {
                provider = viewModel.Provider.ToString()
            });
        }

        [HttpGet("server/storage/{provider}")]
        public async Task<IActionResult> StorageProvider(string provider)
        {
            if (!Enum.TryParse(typeof(StorageProvider), provider, out var storageProvider))
            {
                return RedirectToAction(nameof(Storage), new
                {
                    StatusMessage = new StatusMessageModel()
                    {
                        Severity = StatusMessageModel.StatusSeverity.Error,
                        Message = $"{provider} provider is not supported"
                    }.ToString()
                });
            }

            var data = (await _SettingsRepository.GetSettingAsync<StorageSettings>()) ?? new StorageSettings();

            var storageProviderService =
                _StorageProviderServices.SingleOrDefault(service => service.StorageProvider().Equals(storageProvider));

            switch (storageProviderService)
            {
                case null:
                    return RedirectToAction(nameof(Storage), new
                    {
                        StatusMessage = new StatusMessageModel()
                        {
                            Severity = StatusMessageModel.StatusSeverity.Error,
                            Message = $"{storageProvider} is not supported"
                        }.ToString()
                    });
                case AzureBlobStorageFileProviderService fileProviderService:
                    return View(nameof(EditAzureBlobStorageStorageProvider),
                        fileProviderService.GetProviderConfiguration(data));

//                case AmazonS3FileProviderService fileProviderService:
//                    return View(nameof(EditAmazonS3StorageProvider),
//                        fileProviderService.GetProviderConfiguration(data));
//
//                case GoogleCloudStorageFileProviderService fileProviderService:
//                    return View(nameof(EditGoogleCloudStorageStorageProvider),
//                        fileProviderService.GetProviderConfiguration(data));

                case FileSystemFileProviderService fileProviderService:
                    return View(nameof(EditFileSystemStorageProvider),
                        fileProviderService.GetProviderConfiguration(data));
            }

            return NotFound();
        }


        [HttpPost("server/storage/AzureBlobStorage")]
        public async Task<IActionResult> EditAzureBlobStorageStorageProvider(AzureBlobStorageConfiguration viewModel)
        {
            return await SaveStorageProvider(viewModel, BTCPayServer.Storage.Models.StorageProvider.AzureBlobStorage);
        }

//        [HttpPost("server/storage/AmazonS3")]
//        public async Task<IActionResult> EditAmazonS3StorageProvider(AmazonS3StorageConfiguration viewModel)
//        {
//            return await SaveStorageProvider(viewModel, BTCPayServer.Storage.Models.StorageProvider.AmazonS3);
//        }
//
//        [HttpPost("server/storage/GoogleCloudStorage")]
//        public async Task<IActionResult> EditGoogleCloudStorageStorageProvider(
//            GoogleCloudStorageConfiguration viewModel)
//        {
//            return await SaveStorageProvider(viewModel, BTCPayServer.Storage.Models.StorageProvider.GoogleCloudStorage);
//        }

        [HttpPost("server/storage/FileSystem")]
        public async Task<IActionResult> EditFileSystemStorageProvider(FileSystemStorageConfiguration viewModel)
        {
            return await SaveStorageProvider(viewModel, BTCPayServer.Storage.Models.StorageProvider.FileSystem);
        }

        private async Task<IActionResult> SaveStorageProvider(IBaseStorageConfiguration viewModel,
            StorageProvider storageProvider)
        {
            if (!ModelState.IsValid)
            {
                return View(viewModel);
            }

            var data = (await _SettingsRepository.GetSettingAsync<StorageSettings>()) ?? new StorageSettings();
            data.Provider = storageProvider;
            data.Configuration = JObject.FromObject(viewModel);
            await _SettingsRepository.UpdateSetting(data);
            TempData["StatusMessage"] = new StatusMessageModel()
            {
                Severity = StatusMessageModel.StatusSeverity.Success,
                Message = "Storage settings updated successfully"
            }.ToString();
            return View(viewModel);
        }
    }
}
