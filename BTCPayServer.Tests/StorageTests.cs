using System;
using System.IO;
using System.Threading.Tasks;
using BTCPayServer.Controllers;
using BTCPayServer.Models;
using BTCPayServer.Models.ServerViewModels;
using BTCPayServer.Storage.Models;
using BTCPayServer.Storage.Services.Providers.AzureBlobStorage.Configuration;
using BTCPayServer.Storage.Services.Providers.FileSystemStorage.Configuration;
using BTCPayServer.Storage.ViewModels;
using BTCPayServer.Tests.Logging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Internal;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestPlatform.CommunicationUtilities.Resources;
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

//                //For some reason, the tests cache something on circleci and this is set by default
//                //Initially, there is no configuration, make sure we display the choices available to configure
//                Assert.IsType<StorageSettings>(Assert.IsType<ViewResult>(await controller.Storage()).Model);
//
//                //the file list should tell us it's not configured:
//                var viewFilesViewModelInitial =
//                    Assert.IsType<ViewFilesViewModel>(Assert.IsType<ViewResult>(await controller.Files()).Model);
//                Assert.False(viewFilesViewModelInitial.StorageConfigured);


                //Once we select a provider, redirect to its view
                var localResult = Assert
                    .IsType<RedirectToActionResult>(controller.Storage(new StorageSettings()
                    {
                        Provider = StorageProvider.FileSystem
                    }));
                Assert.Equal(nameof(ServerController.StorageProvider), localResult.ActionName);
                Assert.Equal(StorageProvider.FileSystem.ToString(), localResult.RouteValues["provider"]);


                var AmazonS3result = Assert
                    .IsType<RedirectToActionResult>(controller.Storage(new StorageSettings()
                    {
                        Provider = StorageProvider.AmazonS3
                    }));
                Assert.Equal(nameof(ServerController.StorageProvider), AmazonS3result.ActionName);
                Assert.Equal(StorageProvider.AmazonS3.ToString(), AmazonS3result.RouteValues["provider"]);

                var GoogleResult = Assert
                    .IsType<RedirectToActionResult>(controller.Storage(new StorageSettings()
                    {
                        Provider = StorageProvider.GoogleCloudStorage
                    }));
                Assert.Equal(nameof(ServerController.StorageProvider), GoogleResult.ActionName);
                Assert.Equal(StorageProvider.GoogleCloudStorage.ToString(), GoogleResult.RouteValues["provider"]);


                var AzureResult = Assert
                    .IsType<RedirectToActionResult>(controller.Storage(new StorageSettings()
                    {
                        Provider = StorageProvider.AzureBlobStorage
                    }));
                Assert.Equal(nameof(ServerController.StorageProvider), AzureResult.ActionName);
                Assert.Equal(StorageProvider.AzureBlobStorage.ToString(), AzureResult.RouteValues["provider"]);

                //Cool, we get redirected to the config pages
                //Let's configure this stuff

                //Let's try and cheat and go to an invalid storage provider config
                Assert.Equal(nameof(Storage), (Assert
                    .IsType<RedirectToActionResult>(await controller.StorageProvider("I am not a real provider"))
                    .ActionName));

                //ok no more messing around, let's configure this shit. 
                var fileSystemStorageConfiguration = Assert.IsType<FileSystemStorageConfiguration>(Assert
                    .IsType<ViewResult>(await controller.StorageProvider(StorageProvider.FileSystem.ToString()))
                    .Model);

                //local file system does not need config, easy days!
                Assert.IsType<ViewResult>(
                    await controller.EditFileSystemStorageProvider(fileSystemStorageConfiguration));

                //ok cool, let's see if this got set right
                var shouldBeRedirectingToLocalStorageConfigPage =
                    Assert.IsType<RedirectToActionResult>(await controller.Storage());
                Assert.Equal(nameof(StorageProvider), shouldBeRedirectingToLocalStorageConfigPage.ActionName);
                Assert.Equal(StorageProvider.FileSystem,
                    shouldBeRedirectingToLocalStorageConfigPage.RouteValues["provider"]);


                //if we tell the settings page to force, it should allow us to select a new provider
                Assert.IsType<ChooseStorageViewModel>(Assert.IsType<ViewResult>(await controller.Storage(true)).Model);

                //awesome, now let's see if the files result says we're all set up
                var viewFilesViewModel =
                    Assert.IsType<ViewFilesViewModel>(Assert.IsType<ViewResult>(await controller.Files()).Model);
                Assert.True(viewFilesViewModel.StorageConfigured);
                Assert.Empty(viewFilesViewModel.Files);
            }
        }

        [Fact]
        [Trait("Integration", "Integration")]
        public async void CanUseLocalProviderFiles()
        {
            using (var tester = ServerTester.Create())
            {
                tester.Start();
                var user = tester.NewAccount();
                user.GrantAccess();
                var controller = tester.PayTester.GetController<ServerController>(user.UserId, user.StoreId);

                var fileSystemStorageConfiguration = Assert.IsType<FileSystemStorageConfiguration>(Assert
                    .IsType<ViewResult>(await controller.StorageProvider(StorageProvider.FileSystem.ToString()))
                    .Model);
                Assert.IsType<ViewResult>(
                    await controller.EditFileSystemStorageProvider(fileSystemStorageConfiguration));
                
                var shouldBeRedirectingToLocalStorageConfigPage =
                    Assert.IsType<RedirectToActionResult>(await controller.Storage());
                Assert.Equal(nameof(StorageProvider), shouldBeRedirectingToLocalStorageConfigPage.ActionName);
                Assert.Equal(StorageProvider.FileSystem,
                    shouldBeRedirectingToLocalStorageConfigPage.RouteValues["provider"]);


                await CanUploadRemoveFiles(controller);
            }
        }

        [Fact]
        [Trait("ExternalIntegration", "ExternalIntegration")]
        public async Task CanUseAzureBlobStorage()
        {
            using (var tester = ServerTester.Create())
            {
                tester.Start();
                var user = tester.NewAccount();
                user.GrantAccess();
                var controller = tester.PayTester.GetController<ServerController>(user.UserId, user.StoreId);
                var azureBlobStorageConfiguration = Assert.IsType<AzureBlobStorageConfiguration>(Assert
                    .IsType<ViewResult>(await controller.StorageProvider(StorageProvider.AzureBlobStorage.ToString()))
                    .Model);

                azureBlobStorageConfiguration.ConnectionString = GetFromSecrets("AzureBlobStorageConnectionString");
                azureBlobStorageConfiguration.ContainerName = "testscontainer";
                Assert.IsType<ViewResult>(
                    await controller.EditAzureBlobStorageStorageProvider(azureBlobStorageConfiguration));


                var shouldBeRedirectingToAzureStorageConfigPage =
                    Assert.IsType<RedirectToActionResult>(await controller.Storage());
                Assert.Equal(nameof(StorageProvider), shouldBeRedirectingToAzureStorageConfigPage.ActionName);
                Assert.Equal(StorageProvider.AzureBlobStorage,
                    shouldBeRedirectingToAzureStorageConfigPage.RouteValues["provider"]);

                //seems like azure config worked, let's see if the conn string was actually saved

                Assert.Equal(azureBlobStorageConfiguration.ConnectionString, Assert
                    .IsType<AzureBlobStorageConfiguration>(Assert
                        .IsType<ViewResult>(
                            await controller.StorageProvider(StorageProvider.AzureBlobStorage.ToString()))
                        .Model).ConnectionString);
                
                

                await CanUploadRemoveFiles(controller);
            }
        }
        
        
        private async Task CanUploadRemoveFiles(ServerController controller)
        {
            var filename = "uploadtestfile.txt";
            var fileContent = "content";
            File.WriteAllText(filename, fileContent);

            var fileInfo = new FileInfo(filename);
            var formFile = new FormFile(
                new FileStream(filename, FileMode.OpenOrCreate),
                0,
                fileInfo.Length, fileInfo.Name, fileInfo.Name)
            {
                Headers = new HeaderDictionary()
            };
            formFile.ContentType = "text/plain";
            formFile.ContentDisposition = $"form-data; name=\"file\"; filename=\"{fileInfo.Name}\"";
            var uploadFormFileResult = Assert.IsType<RedirectToActionResult>(await controller.CreateFile(formFile));
            Assert.True(uploadFormFileResult.RouteValues.ContainsKey("fileId"));
            var fileId = uploadFormFileResult.RouteValues["fileId"].ToString();
            Assert.Equal("Files", uploadFormFileResult.ActionName);

            var viewFilesViewModel =
                Assert.IsType<ViewFilesViewModel>(Assert.IsType<ViewResult>(await controller.Files(fileId)).Model);

            Assert.NotEmpty(viewFilesViewModel.Files);
            Assert.Equal(fileId, viewFilesViewModel.SelectedFileId);
            Assert.NotEmpty(viewFilesViewModel.DirectFileUrl);


            var net = new System.Net.WebClient();
            var data = await net.DownloadStringTaskAsync(new Uri(viewFilesViewModel.DirectFileUrl));
            Assert.Equal(fileContent, data);

            Assert.Equal(StatusMessageModel.StatusSeverity.Success, new StatusMessageModel(Assert
                .IsType<RedirectToActionResult>(await controller.DeleteFile(fileId))
                .RouteValues["statusMessage"].ToString()).Severity);

            viewFilesViewModel =
                Assert.IsType<ViewFilesViewModel>(Assert.IsType<ViewResult>(await controller.Files(fileId)).Model);

            Assert.Null(viewFilesViewModel.DirectFileUrl);
            Assert.Null(viewFilesViewModel.SelectedFileId);
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
