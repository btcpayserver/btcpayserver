using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Configuration;
using BTCPayServer.Contracts;
using BTCPayServer.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Controllers
{
    public partial class ServerController
    {
        [HttpGet("server/extensions")]
        public async Task<IActionResult> ListExtensions(
            [FromServices] ExtensionService extensionService,
            [FromServices] BTCPayServerOptions btcPayServerOptions,
            string remote = "btcpayserver/btcpayserver-extensions")
        {
            IEnumerable<ExtensionService.AvailableExtension> availableExtensions;
            try
            {
                availableExtensions = await extensionService.GetRemoteExtensions(remote);
            }
            catch (Exception e)
            {
                TempData.SetStatusMessageModel(new StatusMessageModel()
                {
                    Severity = StatusMessageModel.StatusSeverity.Error,
                    Message = "The remote could not be reached"
                });
                availableExtensions = Array.Empty<ExtensionService.AvailableExtension>();
            }
            var res = new ListExtensionsViewModel()
            {
                Installed = extensionService.LoadedExtensions,
                Available = availableExtensions,
                Remote = remote,
                Commands = extensionService.GetPendingCommands(),
                CanShowRestart = btcPayServerOptions.DockerDeployment
            };
            return View(res);
        }

        public class ListExtensionsViewModel
        {
            public string Remote { get; set; }
            public IEnumerable<IBTCPayServerExtension> Installed { get; set; }
            public IEnumerable<ExtensionService.AvailableExtension> Available { get; set; }
            public (string command, string extension)[] Commands { get; set; }
            public bool CanShowRestart { get; set; }
        }

        [HttpPost("server/extensions/uninstall")]
        public IActionResult UnInstallExtension(
            [FromServices] ExtensionService extensionService, string extension)
        {
            extensionService.UninstallExtension(extension);
            TempData.SetStatusMessageModel(new StatusMessageModel()
            {
                Message = "Extension scheduled to be uninstalled",
                Severity = StatusMessageModel.StatusSeverity.Success
            });

            return RedirectToAction("ListExtensions");
        }
        [HttpPost("server/extensions/cancel")]
        public IActionResult CancelExtensionCommands(
            [FromServices] ExtensionService extensionService, string extension)
        {
            extensionService.CancelCommands(extension);
            TempData.SetStatusMessageModel(new StatusMessageModel()
            {
                Message = "Updated",
                Severity = StatusMessageModel.StatusSeverity.Success
            });

            return RedirectToAction("ListExtensions");
        }

        [HttpPost("server/extensions/install")]
        public async Task<IActionResult> InstallExtension(
            [FromServices] ExtensionService extensionService, string remote, string extension)
        {
            try
            {
                await extensionService.DownloadRemoteExtension(remote, extension);
                extensionService.InstallExtension(extension);
                TempData.SetStatusMessageModel(new StatusMessageModel()
                {
                    Message = "Extension scheduled to be installed.",
                    Severity = StatusMessageModel.StatusSeverity.Success
                });
            }
            catch (Exception e)
            {
                TempData.SetStatusMessageModel(new StatusMessageModel()
                {
                    Message = "The extension could not be downloaded. Try again later.", Severity = StatusMessageModel.StatusSeverity.Error
                });
            }

            return RedirectToAction("ListExtensions");
        }


        [HttpPost("server/extensions/upload")]
        public async Task<IActionResult> UploadExtension([FromServices] ExtensionService extensionService,
            List<IFormFile> files)
        {
            foreach (var formFile in files.Where(file => file.Length > 0))
            {
                await extensionService.UploadExtension(formFile);
                extensionService.InstallExtension(formFile.FileName.TrimEnd(ExtensionManager.BTCPayExtensionSuffix,
                    StringComparison.InvariantCultureIgnoreCase));
            }

            return RedirectToAction("ListExtensions",
                new {StatusMessage = "Files uploaded, restart server to load extensions"});
        }
    }
}
