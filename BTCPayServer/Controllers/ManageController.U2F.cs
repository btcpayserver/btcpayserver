using System;
using System.Threading.Tasks;
using BTCPayServer.Models;
using BTCPayServer.Services.U2F.Models;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Controllers
{
    public partial class ManageController
    {
        [HttpGet]
        public async Task<IActionResult> U2FAuthentication(string statusMessage = null)
        {
            return View(new U2FAuthenticationViewModel()
            {
                StatusMessage = statusMessage,
                Devices = await _u2FService.GetDevices(_userManager.GetUserId(User))
            });
        }

        [HttpGet]
        public async Task<IActionResult> RemoveU2FDevice(string id)
        {
            await _u2FService.RemoveDevice(id, _userManager.GetUserId(User));
            return RedirectToAction("U2FAuthentication", new
            {
                StatusMessage = "Device removed"
            });
        }

        [HttpGet]
        public IActionResult AddU2FDevice(string name)
        {
            if (!Request.IsHttps)
            {
                return RedirectToAction("U2FAuthentication", new
                {
                    StatusMessage = new StatusMessageModel()
                    {
                        Severity = StatusMessageModel.StatusSeverity.Error,
                        Message = "Cannot register U2F device while not on https"
                    }
                });
            }

            var serverRegisterResponse = _u2FService.StartDeviceRegistration(_userManager.GetUserId(User),
                Request.GetAbsoluteUriNoPathBase().ToString().TrimEnd('/'));

            return View(new AddU2FDeviceViewModel()
            {
                AppId = serverRegisterResponse.AppId,
                Challenge = serverRegisterResponse.Challenge,
                Version = serverRegisterResponse.Version,
                Name = string.IsNullOrEmpty(name) ? "Unlabelled U2F Device" : name
            });
        }

        [HttpPost]
        public async Task<IActionResult> AddU2FDevice(AddU2FDeviceViewModel viewModel)
        {
            try
            {
                if (await _u2FService.CompleteRegistration(_userManager.GetUserId(User), viewModel.DeviceResponse,
                    viewModel.Name))
                {
                    return RedirectToAction("U2FAuthentication", new
                    {
                        StatusMessage = "Device added!"
                    });
                }

                throw new Exception("Could not add device.");
            }
            catch (Exception e)
            {
                return RedirectToAction("U2FAuthentication", new
                {
                    StatusMessage = new StatusMessageModel()
                    {
                        Severity = StatusMessageModel.StatusSeverity.Error,
                        Message = e.Message
                    }
                });
            }
        }
    }
}
