using System.ComponentModel.DataAnnotations;
using BTCPayServer.Storage.Services.Providers.Models;
using TwentyTwenty.Storage.Azure;

namespace BTCPayServer.Storage.Services.Providers.AzureBlobStorage.Configuration
{
    public class AzureBlobStorageConfiguration : AzureProviderOptions, IBaseStorageConfiguration
    {
        [Required]
        [MinLength(3)]
        [MaxLength(63)]
         [RegularExpression(@"[a-z0-9-]+", 
                 ErrorMessage = "Characters must be lowercase or digits or -")]
        public string ContainerName { get; set; }
    }
}
