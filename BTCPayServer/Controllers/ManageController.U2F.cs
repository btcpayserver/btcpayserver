using System;
using System.Threading.Tasks;
using BTCPayServer.Models;
using BTCPayServer.U2F.Models;
using Microsoft.AspNetCore.Mvc;
using U2F.Core.Exceptions;

namespace BTCPayServer.Controllers
{
    public partial class ManageController
    {
        [HttpGet]
        public async Task<IActionResult> U2FAuthentication()
        {
            return View(new U2FAuthenticationViewModel()
            {
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
            if (!_btcPayServerEnvironment.IsSecure)
            {
                TempData.SetStatusMessageModel(new StatusMessageModel()
                {
                    Severity = StatusMessageModel.StatusSeverity.Error,
                    Message = "Cannot register U2F device while not on https or tor"
                });
                return RedirectToAction("U2FAuthentication");
            }

            var serverRegisterResponse = _u2FService.StartDeviceRegistration(_userManager.GetUserId(User),
                Request.GetAbsoluteUriNoPathBase().ToString().TrimEnd('/'));

            return View(new AddU2FDeviceViewModel()
            {
                AppId = serverRegisterResponse.AppId,
                Challenge = serverRegisterResponse.Challenge,
                Version = serverRegisterResponse.Version,
                Name = name
            });
        }

        [HttpPost]
        public async Task<IActionResult> AddU2FDevice(AddU2FDeviceViewModel viewModel)
        {
            var errorMessage = string.Empty;
            try
            {
                if (await _u2FService.CompleteRegistration(_userManager.GetUserId(User), viewModel.DeviceResponse,
                    string.IsNullOrEmpty(viewModel.Name) ? "Unlabelled U2F Device" : viewModel.Name))
                {
                    TempData[WellKnownTempData.SuccessMessage] = "Device added!";
                    return RedirectToAction("U2FAuthentication");
                }
            }
            catch (U2fException e)
            {
                errorMessage = e.Message;
            }

            TempData.SetStatusMessageModel(new StatusMessageModel()
            {
                Severity = StatusMessageModel.StatusSeverity.Error,
                Message = string.IsNullOrEmpty(errorMessage) ? "Could not add device." : errorMessage
            });
            return RedirectToAction("U2FAuthentication");
        }
    }
}
