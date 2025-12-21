using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.Storage.Services.Providers.AzureBlobStorage.Configuration
{
    public class AzureBlobStorageConfigurationMetadata
    {
        [Required]
        [AzureBlobStorageConnectionStringValidator]
        public string ConnectionString { get; set; }
    }
}
