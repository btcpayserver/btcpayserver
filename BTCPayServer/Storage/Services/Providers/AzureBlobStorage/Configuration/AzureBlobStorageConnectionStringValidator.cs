using System;
using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.Storage.Services.Providers.AzureBlobStorage.Configuration
{
    public class AzureBlobStorageConnectionStringValidator : ValidationAttribute
    {
        protected override ValidationResult IsValid(object value, ValidationContext validationContext)
        {
            try
            {
                new Azure.Storage.Blobs.BlobClient(value as string, "unusedcontainer", "unusedblob");
                return ValidationResult.Success;
            }
            catch (Exception e)
            {
                return new ValidationResult(e.Message);
            }
        }
    }
}
