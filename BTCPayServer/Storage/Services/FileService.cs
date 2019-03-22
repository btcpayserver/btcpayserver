using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Services;
using BTCPayServer.Storage.Models;
using BTCPayServer.Storage.Services.Providers;
using Microsoft.AspNetCore.Http;

namespace BTCPayServer.Storage.Services
{
    public class FileService
    {
        private readonly StoredFileRepository _FileRepository;
        private readonly IEnumerable<IStorageProviderService> _providers;
        private readonly SettingsRepository _SettingsRepository;

        public FileService(StoredFileRepository fileRepository, IEnumerable<IStorageProviderService> providers,
            SettingsRepository settingsRepository)
        {
            _FileRepository = fileRepository;
            _providers = providers;
            _SettingsRepository = settingsRepository;
        }

        public async Task<StoredFile> AddFile(IFormFile file)
        {
            var settings = await _SettingsRepository.GetSettingAsync<StorageSettings>();
            var provider = GetProvider(settings);

            var storedFile = await provider.AddFile(file, settings);
            await _FileRepository.AddFile(storedFile);
            return storedFile;
        }

        public async Task<string> GetFileBase64(string fileId)
        {
            var settings = await _SettingsRepository.GetSettingAsync<StorageSettings>();
            var provider = GetProvider(settings);
            var storedFile = await _FileRepository.GetFile(fileId);
            return await provider.GetFileBase64(storedFile, settings);
        }

        public async Task RemoveFile(string fileId)
        {
            var settings = await _SettingsRepository.GetSettingAsync<StorageSettings>();
            var provider = GetProvider(settings);
            var storedFile = await _FileRepository.GetFile(fileId);
            await provider.RemoveFile(storedFile, settings);
        }

        private IStorageProviderService GetProvider(StorageSettings storageSettings)
        {
            return _providers.FirstOrDefault((service) => service.StorageProvider().Equals(storageSettings.Provider));
        }
    }
}
