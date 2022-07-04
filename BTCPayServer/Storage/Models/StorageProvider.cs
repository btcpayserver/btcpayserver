using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.Storage.Models
{
    public enum StorageProvider
    {
        [Display(Name = "Azure Blob Storage")]
        AzureBlobStorage = 0,
        
        [Display(Name = "Amazon S3")]
        AmazonS3 = 1,
        
        [Display(Name = "Google Cloud Storage")]
        GoogleCloudStorage = 2,
        
        [Display(Name = "Local File System")]
        FileSystem = 3
    }
}
