namespace BTCPayServer.Storage.Models
{
    public enum StorageProvider
    {
        AzureBlobStorage=0,
        AmazonS3 =1,
        GoogleCloudStorage =2,
        FileSystem =3
    }
}
