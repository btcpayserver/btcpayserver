using BTCPayServer.Controllers;
using BTCPayServer.Storage.Models;
using BTCPayServer.Storage.Services.Providers.AzureBlobStorage.Configuration;
using BTCPayServer.Storage.Services.Providers.FileSystemStorage.Configuration;
using BTCPayServer.Tests.Logging;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Xunit;
using Xunit.Abstractions;

namespace BTCPayServer.Tests
{
    public class StorageTests
    {
    
          public StorageTests(ITestOutputHelper helper)
        {
            Logs.Tester = new XUnitLog(helper) {Name = "Tests"};
            Logs.LogProvider = new XUnitLogProvider(helper);
        }

        [Fact]
        [Trait("Integration", "Integration")]
        public async void CanConfigureStorage()
        {
            using (var tester = ServerTester.Create())
            {
                tester.Start();
                var user = tester.NewAccount();
                user.GrantAccess();
                var controller = tester.PayTester.GetController<ServerController>(user.UserId, user.StoreId);

                //Initially, there is no configuration, make sure we display the choices available to configure
                Assert.IsType<StorageSettings>(Assert.IsType<ViewResult>(await controller.Storage()).Model);
                
                //Once we select a provider, redirect to its view
                Assert.Equal(nameof(ServerController.EditFileSystemStorageProvider), (Assert
                    .IsType<RedirectToActionResult>(controller.Storage(new StorageSettings()
                    {
                        Provider = StorageProvider.FileSystem
                    })).ActionName));
                Assert.Equal(nameof(ServerController.EditAmazonS3StorageProvider), (Assert
                    .IsType<RedirectToActionResult>(controller.Storage(new StorageSettings()
                    {
                        Provider = StorageProvider.AmazonS3
                    })).ActionName));
                Assert.Equal(nameof(ServerController.EditAzureBlobStorageStorageProvider), (Assert
                    .IsType<RedirectToActionResult>(controller.Storage(new StorageSettings()
                    {
                        Provider = StorageProvider.AzureBlobStorage
                    })).ActionName));
                Assert.Equal(nameof(ServerController.EditGoogleCloudStorageStorageProvider), (Assert
                    .IsType<RedirectToActionResult>(controller.Storage(new StorageSettings()
                    {
                        Provider = StorageProvider.GoogleCloudStorage
                    })).ActionName));
                
                
                
                //Cool, we get redirected to the config pages
                //Let's configure this stuff
                
                //Let's try and cheat and go to an invalid storage provider config
                Assert.Equal(nameof(Storage), (Assert
                    .IsType<RedirectToActionResult>(await controller.StorageProvider("I am not a real provider"))
                    .ActionName));
                
                //ok no more messing around, let's configure this shit. 
                var fileSystemStorageConfiguration =   Assert.IsType<FileSystemStorageConfiguration>(Assert
                    .IsType<ViewResult>(await controller.StorageProvider(StorageProvider.FileSystem.ToString()))
                    .Model);
                
                //local file system does not need config, easy days!
                Assert.IsType<ViewResult>(await controller.EditFileSystemStorageProvider(fileSystemStorageConfiguration));
                
                //ok cool, let's see if this got set right
                var shouldBeRedirectingToLocalStorageConfigPage =
                    Assert.IsType<RedirectToActionResult>(await controller.Storage());
                Assert.Equal(nameof(StorageProvider), shouldBeRedirectingToLocalStorageConfigPage.ActionName);
                Assert.Equal(StorageProvider.FileSystem, shouldBeRedirectingToLocalStorageConfigPage.RouteValues["provider"]);
                
                
                //ok we're set up with local file storage. let's change to azure
                
                //if we tell the settings page to force, it should allow us to select a new provider
                Assert.IsType<StorageSettings>(Assert.IsType<ViewResult>(await controller.Storage(true)).Model);
                
                var azureBlobStorageConfiguration =   Assert.IsType<AzureBlobStorageConfiguration>(Assert
                    .IsType<ViewResult>(await controller.StorageProvider(StorageProvider.AzureBlobStorage.ToString()))
                    .Model);

                azureBlobStorageConfiguration.ConnectionString = GetFromSecrets("AzureBlobStorageConnectionString");
                Assert.IsType<ViewResult>(await controller.EditAzureBlobStorageStorageProvider(azureBlobStorageConfiguration));
                
                
                var shouldBeRedirectingToAzureStorageConfigPage =
                    Assert.IsType<RedirectToActionResult>(await controller.Storage());
                Assert.Equal(nameof(StorageProvider), shouldBeRedirectingToLocalStorageConfigPage.ActionName);
                Assert.Equal(StorageProvider.AzureBlobStorage, shouldBeRedirectingToLocalStorageConfigPage.RouteValues["provider"]);
                
               //seems like everything worked, let's see if the conn string was actually saved
               
               Assert.Equal(azureBlobStorageConfiguration.ConnectionString, Assert.IsType<AzureBlobStorageConfiguration>(Assert
                   .IsType<ViewResult>(await controller.StorageProvider(StorageProvider.AzureBlobStorage.ToString()))
                   .Model).ConnectionString);
            }
        }
        
        private static string GetFromSecrets(string key)
        {
            var builder = new ConfigurationBuilder();
            builder.AddUserSecrets("AB0AC1DD-9D26-485B-9416-56A33F268117");
            var config = builder.Build();
            var token = config[key];
            Assert.False(token == null, $"{key} is not set.\n Run \"dotnet user-secrets set {key} <value>\"");
            return token;
        }
    }
}
