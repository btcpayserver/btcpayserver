using System;
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
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Controllers
{
    public partial class ServerController
    {
        [HttpGet("server/storage")]
        public async Task<IActionResult> Storage()
        {
            var savedSettings = await _SettingsRepository.GetSettingAsync<StorageSettings>();
            if (savedSettings == null)
            {
                return View(new StorageSettings());
            }

            return RedirectToAction("StorageProvider", new
            {
                provider = savedSettings.Provider
            });
        }

        [HttpPost("server/storage")]
        public async Task<IActionResult> Storage(StorageSettings viewModel)
        {
            return RedirectToAction("StorageProvider", new
            {
                provider = viewModel.Provider
            });
        }

        [Route("server/storage/{provider}")]
        public async Task<IActionResult> StorageProvider(string provider)
        {
            var storageProvider = Enum.Parse(typeof(StorageProvider), provider);
            var data = (await _SettingsRepository.GetSettingAsync<StorageSettings>()) ?? new StorageSettings();

            var storageProviderService =
                _StorageProviderServices.Single(service => service.StorageProvider().Equals(storageProvider));


            var viewName = $"{storageProvider}StorageProvider";
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
        public async Task<IActionResult> StorageProvider(AzureBlobStorageConfiguration viewModel)
        {
            var data = (await _SettingsRepository.GetSettingAsync<StorageSettings>()) ?? new StorageSettings();
            data.Provider = BTCPayServer.Storage.Models.StorageProvider.AzureBlobStorage;
            data.Configuration = viewModel.ConvertConfiguration();

            return View(viewModel);
        }

        [HttpPost("server/storage/AmazonS3")]
        public async Task<IActionResult> StorageProvider(AmazonS3StorageConfiguration viewModel)
        {
            var data = (await _SettingsRepository.GetSettingAsync<StorageSettings>()) ?? new StorageSettings();
            data.Provider = BTCPayServer.Storage.Models.StorageProvider.AmazonS3;
            data.Configuration = viewModel.ConvertConfiguration();
            return View(viewModel);
        }

        [HttpPost("server/storage/GoogleCloudStorage")]
        public async Task<IActionResult> StorageProvider(GoogleCloudStorageConfiguration viewModel)
        {
            var data = (await _SettingsRepository.GetSettingAsync<StorageSettings>()) ?? new StorageSettings();
            data.Provider = BTCPayServer.Storage.Models.StorageProvider.GoogleCloudStorage;
            data.Configuration = viewModel.ConvertConfiguration();
            return View(viewModel);
        }

        [HttpPost("server/storage/FileSystem")]
        public async Task<IActionResult> StorageProvider(FileSystemStorageConfiguration viewModel)
        {
            var data = (await _SettingsRepository.GetSettingAsync<StorageSettings>()) ?? new StorageSettings();
            data.Provider = BTCPayServer.Storage.Models.StorageProvider.FileSystem;
            data.Configuration = viewModel.ConvertConfiguration();
            return View(viewModel);
        }
    }
}
