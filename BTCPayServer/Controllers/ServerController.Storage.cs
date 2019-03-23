using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Controllers
{
    public partial class ServerController
    {
        [HttpGet("server/files/{fileId?}")]
        public async Task<IActionResult> Files(string fileId, string statusMessage)
        {
            TempData["StatusMessage"] = statusMessage;
            return View(new ViewFilesViewModel()
            {
                Files = await _StoredFileRepository.GetFiles(),
                SelectedFileId = fileId,
                FileUrl = string.IsNullOrEmpty(fileId) ? null : await _FileService.GetFileUrl(fileId)
            });
        }

        [HttpGet("server/files/{fileId}/delete")]
        public async Task<IActionResult> DeleteFile(string fileId)
        {
            try
            {
                await _FileService.RemoveFile(fileId);
                return RedirectToAction("Files", new
                {
                    fileId= "",
                    statusMessage = "File removed"
                });
            }
            catch (Exception e)
            {
                return RedirectToAction("Files", new
                {
                    statusMessage = $"Error:{e.Message}"
                });
            }
        }


        [HttpPost("server/files/upload")]
        public async Task<IActionResult> CreateFile(IFormFile file)
        {
            var newFile = await _FileService.AddFile(file);
            return RedirectToAction("Files", new
            {
                statusMessage = "File added!",
                fileId = newFile.Id
            });
        }

        public class ViewFilesViewModel
        {
            public List<StoredFile> Files { get; set; }
            public string FileUrl { get; set; }
            public string SelectedFileId { get; set; }
        }

        [HttpGet("server/storage")]
        public async Task<IActionResult> Storage(bool forceChoice = false)
        {
            var savedSettings = await _SettingsRepository.GetSettingAsync<StorageSettings>();
            if (forceChoice || savedSettings == null)
            {
                return View(new StorageSettings()
                {
                    Provider = savedSettings?.Provider ?? BTCPayServer.Storage.Models.StorageProvider.FileSystem
                });
            }

            return RedirectToAction("StorageProvider", new
            {
                provider = savedSettings.Provider
            });
        }

        [HttpPost("server/storage")]
        public async Task<IActionResult> Storage(StorageSettings viewModel)
        {
            return RedirectToAction("StorageProvider", "Server", new
            {
                provider = viewModel.Provider.ToString()
            });
        }

        [HttpGet("server/storage/{provider}")]
        public async Task<IActionResult> StorageProvider(string provider)
        {
            var storageProvider = Enum.Parse(typeof(StorageProvider), provider);
            var data = (await _SettingsRepository.GetSettingAsync<StorageSettings>()) ?? new StorageSettings();

            var storageProviderService =
                _StorageProviderServices.Single(service => service.StorageProvider().Equals(storageProvider));

            var viewName = $"Edit{storageProvider}StorageProvider";
            switch (storageProviderService)
            {
                case AzureBlobStorageFileProviderService fileProviderService:
                    return View(viewName, fileProviderService.GetProviderConfiguration(data));

                case AmazonS3FileProviderService fileProviderService:
                    return View(viewName, fileProviderService.GetProviderConfiguration(data));

                case GoogleCloudStorageFileProviderService fileProviderService:
                    return View(viewName, fileProviderService.GetProviderConfiguration(data));

                case FileSystemFileProviderService fileProviderService:
                    return View(viewName, fileProviderService.GetProviderConfiguration(data));
            }

            return NotFound();
        }


        [HttpPost("server/storage/AzureBlobStorage")]
        public async Task<IActionResult> EditAzureBlobStorageStorageProvider(AzureBlobStorageConfiguration viewModel)
        {
            return await SaveStorageProvider(viewModel, BTCPayServer.Storage.Models.StorageProvider.AzureBlobStorage);
        }

        [HttpPost("server/storage/AmazonS3")]
        public async Task<IActionResult> EditAmazonS3StorageProvider(AmazonS3StorageConfiguration viewModel)
        {
            return await SaveStorageProvider(viewModel, BTCPayServer.Storage.Models.StorageProvider.AmazonS3);
        }

        [HttpPost("server/storage/GoogleCloudStorage")]
        public async Task<IActionResult> EditGoogleCloudStorageStorageProvider(
            GoogleCloudStorageConfiguration viewModel)
        {
            return await SaveStorageProvider(viewModel, BTCPayServer.Storage.Models.StorageProvider.GoogleCloudStorage);
        }

        [HttpPost("server/storage/FileSystem")]
        public async Task<IActionResult> EditFileSystemStorageProvider(FileSystemStorageConfiguration viewModel)
        {
            return await SaveStorageProvider(viewModel, BTCPayServer.Storage.Models.StorageProvider.FileSystem);
        }

        private async Task<IActionResult> SaveStorageProvider(IBaseStorageConfiguration viewModel,
            StorageProvider storageProvider)
        {
            var data = (await _SettingsRepository.GetSettingAsync<StorageSettings>()) ?? new StorageSettings();
            data.Provider = storageProvider;
            data.Configuration = JObject.FromObject(viewModel);
            await _SettingsRepository.UpdateSetting(data);
            TempData["StatusMessage"] = "Storage settings updated successfully";
            return View(viewModel);
        }
    }
}
