using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using BTCPayServer.Configuration;
using BTCPayServer.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;

namespace BTCPayServer
{
    public class ExtensionService
    {
        private readonly BTCPayServerOptions _btcPayServerOptions;
        private readonly HttpClient _githubClient;

        public ExtensionService(IEnumerable<IBTCPayServerExtension> btcPayServerExtensions,
            IHttpClientFactory httpClientFactory, BTCPayServerOptions btcPayServerOptions)
        {
            LoadedExtensions = btcPayServerExtensions;
            _githubClient = httpClientFactory.CreateClient();
            _githubClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("btcpayserver", "1"));
            _btcPayServerOptions = btcPayServerOptions;
        }

        public IEnumerable<IBTCPayServerExtension> LoadedExtensions { get; }

        public async Task<IEnumerable<AvailableExtension>> GetRemoteExtensions(string remote)
        {
            var resp = await _githubClient
                .GetStringAsync(new Uri($"https://api.github.com/repos/{remote}/contents"));
            var files = JsonConvert.DeserializeObject<GithubFile[]>(resp);
            return await Task.WhenAll(files.Where(file => file.Name.EndsWith($"{ExtensionManager.BTCPayExtensionSuffix}.json", StringComparison.InvariantCulture)).Select(async file =>
            {
                return await _githubClient.GetStringAsync(file.DownloadUrl).ContinueWith(
                    task => JsonConvert.DeserializeObject<AvailableExtension>(task.Result), TaskScheduler.Current);
            }));
        }

        public async Task DownloadRemoteExtension(string remote, string extension)
        {
            var dest = _btcPayServerOptions.ExtensionDir;
            var resp = await _githubClient
                .GetStringAsync(new Uri($"https://api.github.com/repos/{remote}/contents"));
            var files = JsonConvert.DeserializeObject<GithubFile[]>(resp);
            var ext = files.SingleOrDefault(file => file.Name == $"{extension}{ExtensionManager.BTCPayExtensionSuffix}");
            if (ext is null)
            {
                throw new Exception("Extension not found on remote");
            }

            var filedest = Path.Combine(dest, ext.Name);
            Directory.CreateDirectory(Path.GetDirectoryName(filedest));
            new WebClient().DownloadFile(new Uri(ext.DownloadUrl), filedest);
        }

        public void InstallExtension(string extension)
        {
            var dest = _btcPayServerOptions.ExtensionDir;
            UninstallExtension(extension);
            ExtensionManager.QueueCommands(dest, ("install", extension));
        }

        public async Task UploadExtension(IFormFile extension)
        {
            var dest = _btcPayServerOptions.ExtensionDir;
            var filedest = Path.Combine(dest, extension.FileName);
            Directory.CreateDirectory(Path.GetDirectoryName(filedest));
            if (Path.GetExtension(filedest) == ExtensionManager.BTCPayExtensionSuffix)
            {
                await using var stream = new FileStream(filedest, FileMode.Create);
                await extension.CopyToAsync(stream);
            }
        }

        public void UninstallExtension(string extension)
        {
            var dest = _btcPayServerOptions.ExtensionDir;
            ExtensionManager.QueueCommands(dest, ("delete", extension));
        }

        public class AvailableExtension : IBTCPayServerExtension
        {
            public string Identifier { get; set; }
            public string Name { get; set; }
            public Version Version { get; set; }
            public string Description { get; set; }

            public void Execute(IApplicationBuilder applicationBuilder,
                IServiceProvider applicationBuilderApplicationServices)
            {
            }

            public void Execute(IServiceCollection applicationBuilder)
            {
            }
        }

        class GithubFile
        {
            [JsonProperty("name")] public string Name { get; set; }

            [JsonProperty("sha")] public string Sha { get; set; }

            [JsonProperty("download_url")] public string DownloadUrl { get; set; }
        }

        public (string command, string extension)[] GetPendingCommands()
        {
            return ExtensionManager.GetPendingCommands(_btcPayServerOptions.ExtensionDir);
        }

        public  void CancelCommands(string extension)
        {
            ExtensionManager.CancelCommands(_btcPayServerOptions.ExtensionDir, extension);
        }
    }
}
