using System;
using System.Threading.Tasks;
using BTCPayServer.Storage.Models;
using BTCPayServer.Storage.Services.Providers.Models;
using Microsoft.AspNetCore.Http;
using TwentyTwenty.Storage;

namespace BTCPayServer.Storage.Services.Providers
{
    public abstract class
        BaseTwentyTwentyStorageFileProviderServiceBase<TStorageConfiguration> : IStorageProviderService
        where TStorageConfiguration : IBaseStorageConfiguration
    {
        public abstract StorageProvider StorageProvider();

        public virtual async Task<StoredFile> AddFile(IFormFile file, StorageSettings configuration)
        {
            var storageFileName = $"{Guid.NewGuid()}_{file.FileName}";
            var providerConfiguration = GetProviderConfiguration(configuration);
            var provider = await GetStorageProvider(providerConfiguration);
            using (var fileStream = file.OpenReadStream())
            {
                await provider.SaveBlobStreamAsync(providerConfiguration.ContainerName, storageFileName, fileStream,
                    new BlobProperties()
                    {
                        ContentType = file.ContentType,
                        ContentDisposition = file.ContentDisposition,
                        Security = BlobSecurity.Public,
                    });
            }

            return new StoredFile()
            {
                Timestamp = DateTime.Now,
                FileName = file.FileName,
                StorageFileName = storageFileName
            };
        }

        public virtual async Task<string> GetFileUrl(StoredFile storedFile, StorageSettings configuration)
        {
            var providerConfiguration = GetProviderConfiguration(configuration);
            var provider = await GetStorageProvider(providerConfiguration);

            return provider.GetBlobUrl(providerConfiguration.ContainerName, storedFile.StorageFileName);
        }

        public async Task RemoveFile(StoredFile storedFile, StorageSettings configuration)
        {
            var providerConfiguration = GetProviderConfiguration(configuration);
            var provider = await GetStorageProvider(providerConfiguration);
            await provider.DeleteBlobAsync(providerConfiguration.ContainerName, storedFile.StorageFileName);
        }

        public abstract TStorageConfiguration GetProviderConfiguration(StorageSettings configuration);

        protected abstract Task<IStorageProvider> GetStorageProvider(TStorageConfiguration configuration);
    }
}
