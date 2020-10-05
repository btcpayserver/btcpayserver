using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
            string remote = "kukks/btcpayserver-extensions")
        {
            var res = new ListExtensionsViewModel()
            {
                Installed = extensionService.LoadedExtensions,
                Available = await extensionService.GetRemoteExtensions(remote),
                Remote = remote
            };
            return View(res);
        }

        public class ListExtensionsViewModel
        {
            public string Remote { get; set; }
            public IEnumerable<IBTCPayServerExtension> Installed { get; set; }
            public IEnumerable<ExtensionService.AvailableExtension> Available { get; set; }
        }

        [HttpPost("server/extensions/uninstall")]
        public async Task<IActionResult> UnInstallExtension(
            [FromServices] ExtensionService extensionService, string extension)
        {
            await extensionService.UninstallExtension(extension);
            TempData.SetStatusMessageModel(new StatusMessageModel()
            {
                Message = "Extension scheduled to be uninstalled, app restarting.",
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
                await extensionService.InstallExtension(extension);
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
                    Message = e.Message, Severity = StatusMessageModel.StatusSeverity.Error
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
                await extensionService.InstallExtension(formFile.FileName.TrimEnd(".btcpay",
                    StringComparison.InvariantCultureIgnoreCase));
            }

            return RedirectToAction("ListExtensions",
                new {StatusMessage = "Files uploaded, restart server to load extensions"});
        }
    }
}
