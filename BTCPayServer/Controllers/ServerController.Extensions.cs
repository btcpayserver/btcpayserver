using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using BTCPayServer.Configuration;
using BTCPayServer.Contracts;
using BTCPayServer.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json;

namespace BTCPayServer.Controllers
{
    public partial class ServerController
    {
        [HttpGet("server/extensions")]
        public async Task<IActionResult> ListExtensions(
            [FromServices] IHttpClientFactory httpClientFactory,
            [FromServices] IEnumerable<IBTCPayServerExtension> extensions,
            string remote = "kukks/btcpayserver-extensions")
        {
            var httpClient = httpClientFactory.CreateClient();
            httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("btcpayserver", "1"));

            var resp = await httpClient
                .GetStringAsync(new Uri($"https://api.github.com/repos/{remote}/contents"));
            var files = JsonConvert.DeserializeObject<GithubFile[]>(resp);
            var extensionInfoFilesTasks = files.Where(file => file.Name.EndsWith(".btcpay.json")).Select(async file =>
            {
                return await httpClient.GetStringAsync(file.DownloadUrl).ContinueWith(
                    task => JsonConvert.DeserializeObject<AvailableExtension>(task.Result), TaskScheduler.Current);
            });
            var res = new ListExtensionsViewModel()
            {
                Installed = extensions, Available = await Task.WhenAll(extensionInfoFilesTasks), Remote = remote
            };
            return View(res);
        }

        public class ListExtensionsViewModel
        {
            public string Remote { get; set; }
            public IEnumerable<IBTCPayServerExtension> Installed { get; set; }
            public IEnumerable<AvailableExtension> Available { get; set; }
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


        [HttpPost("server/extensions/uninstall")]
        public async Task<IActionResult> UnInstallExtension(
            [FromServices] IHostApplicationLifetime applicationLifetime,
            [FromServices] BTCPayServerOptions options, string extension)
        {
            var dest = options.ExtensionDir;
            if (Directory.Exists(Path.Combine(dest, extension)))
            {
                ExtensionManager.QueueCommands(dest, new []
                {
                    ("delete", extension),
                });
                TempData.SetStatusMessageModel(new StatusMessageModel()
                {
                    Message = "Extension scheduled to be uninstalled, app restarting.",
                    Severity = StatusMessageModel.StatusSeverity.Success
                });
                
                _ = Task.Run(async () =>
                {
                    await Task.Delay(2000);
                    applicationLifetime.StopApplication();
                });
            }

            return RedirectToAction("ListExtensions");
        }

        [HttpPost("server/extensions/install")]
        public async Task<IActionResult> InstallExtension(
            [FromServices] IHostApplicationLifetime applicationLifetime,
            [FromServices] IHttpClientFactory httpClientFactory,
            [FromServices] BTCPayServerOptions options, string remote, string extension)
        {
            var dest = options.ExtensionDir;

            var httpClient = httpClientFactory.CreateClient();
            httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("btcpayserver", "1"));

            var resp = await httpClient
                .GetStringAsync(new Uri($"https://api.github.com/repos/{remote}/contents"));
            var files = JsonConvert.DeserializeObject<GithubFile[]>(resp);
            var ext = files.SingleOrDefault(file => file.Name == $"{extension}.btcpay");
            if (ext is null)
            {
                TempData.SetStatusMessageModel(new StatusMessageModel()
                {
                    Severity = StatusMessageModel.StatusSeverity.Error, Message = "Extension not found on remote"
                });
            }

            var filedest = Path.Combine(dest, ext.Name);
            new WebClient().DownloadFile(new Uri(ext.DownloadUrl), filedest);
            if (Directory.Exists(filedest.TrimEnd(".btcpay", StringComparison.InvariantCultureIgnoreCase)))
            {
                ExtensionManager.QueueCommands(dest, new []
                {
                    ("delete", $"{extension.TrimEnd(".btcpay", StringComparison.InvariantCultureIgnoreCase)}"),
                    ("install", ext.Name)
                });
            }
            else
            {
                ZipFile.ExtractToDirectory(filedest,
                    filedest.TrimEnd(".btcpay", StringComparison.InvariantCultureIgnoreCase), true);
                System.IO.File.Delete(filedest);

                TempData.SetStatusMessageModel(new StatusMessageModel()
                {
                    Message = "Files uploaded, app restarting.",
                    Severity = StatusMessageModel.StatusSeverity.Success
                });
            }

            _ = Task.Run(async () =>
            {
                await Task.Delay(2000);
                applicationLifetime.StopApplication();
            });
            return RedirectToAction("ListExtensions");
        }


        [HttpPost("server/extensions/upload")]
        public async Task<IActionResult> UploadExtension([FromServices] BTCPayServerOptions options,
            List<IFormFile> files)
        {
            var dest = options.ExtensionDir;

            foreach (var formFile in files)
            {
                if (formFile.Length > 0)
                {
                    var filedest = Path.Combine(dest, formFile.FileName);
                    if (Path.GetExtension(filedest) == ".btcpay")
                    {
                        if (Directory.Exists(filedest))
                            Directory.CreateDirectory(Path.GetDirectoryName(filedest));
                        using (var stream = new FileStream(filedest, FileMode.Create))
                        {
                            await formFile.CopyToAsync(stream);
                        }

                        ZipFile.ExtractToDirectory(filedest,
                            filedest.TrimEnd(".btcpay", StringComparison.InvariantCultureIgnoreCase), true);
                        System.IO.File.Delete(filedest);
                    }
                }
            }

            return RedirectToAction("ListExtensions",
                new {StatusMessage = "Files uploaded, restart server to load extensions"});
        }
    }

    public static class StringExtensions
    {
        public static string TrimEnd(this string input, string suffixToRemove,
            StringComparison comparisonType)
        {
            if (input != null && suffixToRemove != null
                              && input.EndsWith(suffixToRemove, comparisonType))
            {
                return input.Substring(0, input.Length - suffixToRemove.Length);
            }
            else return input;
        }
    }
}
