using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Models;
using BTCPayServer.Security;
using BTCPayServer.Security.APIKeys;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;
using NBitcoin.DataEncoders;
using YamlDotNet.Core.Tokens;

namespace BTCPayServer.Controllers
{
    public partial class ManageController
    {
        [HttpGet]
        public async Task<IActionResult> APIKeys()
        {
            return View(new ApiKeysViewModel()
            {
                ApiKeyDatas = await _apiKeyRepository.GetKeys(new APIKeyRepository.APIKeyQuery()
                {
                    UserId = new[] {_userManager.GetUserId(User)}
                })
            });
        }
        
        [HttpGet("api-keys/{id}/delete")]
        public async Task<IActionResult> RemoveAPIKey(string id)
        {
            var key = await _apiKeyRepository.GetKey(id);
            if (key == null || key.UserId != _userManager.GetUserId(User))
            {
                return NotFound();
            }
            return View("Confirm", new ConfirmModel()
            {
                Title = "Delete API Key " + (string.IsNullOrEmpty(key.Label) ? string.Empty : key.Label) + "(" + key.Id + ")",
                Description = "Any application using this api key will immediately lose access",
                Action = "Delete",
                ActionUrl = this.Url.ActionLink(nameof(RemoveAPIKeyPost), values: new { id = id })
            });
        }

        [HttpPost("api-keys/{id}/delete")]
        public async Task<IActionResult> RemoveAPIKeyPost(string id)
        {
            var key = await _apiKeyRepository.GetKey(id);
            if (key == null || key.UserId != _userManager.GetUserId(User))
            {
                return NotFound();
            }
            await _apiKeyRepository.Remove(id, _userManager.GetUserId(User));
            TempData.SetStatusMessageModel(new StatusMessageModel()
            {
                Severity = StatusMessageModel.StatusSeverity.Success,
                Message = "API Key removed"
            });
            return RedirectToAction("APIKeys");
        }

        [HttpGet]
        public async Task<IActionResult> AddApiKey()
        {
            if (!_btcPayServerEnvironment.IsSecure)
            {
                TempData.SetStatusMessageModel(new StatusMessageModel()
                {
                    Severity = StatusMessageModel.StatusSeverity.Error,
                    Message = "Cannot generate api keys while not on https or tor"
                });
                return RedirectToAction("APIKeys");
            }

            return View("AddApiKey", await SetViewModelValues(new AddApiKeyViewModel()));
        }

        [HttpGet("~/api-keys/authorize")]
        public async Task<IActionResult> AuthorizeAPIKey(string[] permissions, string applicationName = null,
            bool strict = true, bool selectiveStores = false)
        {
            if (!_btcPayServerEnvironment.IsSecure)
            {
                TempData.SetStatusMessageModel(new StatusMessageModel()
                {
                    Severity = StatusMessageModel.StatusSeverity.Error,
                    Message = "Cannot generate api keys while not on https or tor"
                });
                return RedirectToAction("APIKeys");
            }

            permissions ??= Array.Empty<string>();

            var vm = await SetViewModelValues(new AuthorizeApiKeysViewModel()
            {
                Label = applicationName,
                ServerManagementPermission = permissions.Contains(Permissions.ServerManagement),
                StoreManagementPermission = permissions.Contains(Permissions.StoreManagement),
                PermissionsFormatted = permissions,
                PermissionValues = permissions.Where(s =>
                        !s.Contains(Permissions.StoreManagement, StringComparison.InvariantCultureIgnoreCase) &&
                        s != Permissions.ServerManagement)
                    .Select(s => new AddApiKeyViewModel.PermissionValueItem() {Permission = s, Value = true}).ToList(),
                ApplicationName = applicationName,
                SelectiveStores = selectiveStores,
                Strict = strict,
            });

            vm.ServerManagementPermission = vm.ServerManagementPermission && vm.IsServerAdmin;
            return View(vm);
        }

        [HttpPost("~/api-keys/authorize")]
        public async Task<IActionResult> AuthorizeAPIKey([FromForm] AuthorizeApiKeysViewModel viewModel)
        {
            await SetViewModelValues(viewModel);
            var ar = HandleCommands(viewModel);

            if (ar != null)
            {
                return ar;
            }


            if (viewModel.PermissionsFormatted.Contains(Permissions.ServerManagement))
            {
                if (!viewModel.IsServerAdmin && viewModel.ServerManagementPermission)
                {
                    viewModel.ServerManagementPermission = false;
                }

                if (!viewModel.ServerManagementPermission && viewModel.Strict)
                {
                    ModelState.AddModelError(nameof(viewModel.ServerManagementPermission),
                        "This permission is required for this application.");
                }
            }

            if (viewModel.PermissionsFormatted.Contains(Permissions.StoreManagement))
            {
                if (!viewModel.SelectiveStores &&
                    viewModel.StoreMode == AddApiKeyViewModel.ApiKeyStoreMode.Specific)
                {
                    viewModel.StoreMode = AddApiKeyViewModel.ApiKeyStoreMode.AllStores;
                    ModelState.AddModelError(nameof(viewModel.StoreManagementPermission),
                        "This application does not allow selective store permissions.");
                }

                if (!viewModel.StoreManagementPermission && !viewModel.SpecificStores.Any() && viewModel.Strict)
                {
                    ModelState.AddModelError(nameof(viewModel.StoreManagementPermission),
                        "This permission is required for this application.");
                }
            }

            if (!ModelState.IsValid)
            {
                return View(viewModel);
            }

            switch (viewModel.Command.ToLowerInvariant())
            {
                case "no":
                    return RedirectToAction("APIKeys");
                case "yes":
                    var key = await CreateKey(viewModel);
                    TempData.SetStatusMessageModel(new StatusMessageModel()
                    {
                        Severity = StatusMessageModel.StatusSeverity.Success,
                        Html = $"API key generated! <code>{key.Id}</code>"
                    });
                    return RedirectToAction("APIKeys", new { key = key.Id});
                default: return View(viewModel);
            }
        }

        [HttpPost]
        public async Task<IActionResult> AddApiKey(AddApiKeyViewModel viewModel)
        {
            await SetViewModelValues(viewModel);

            var ar = HandleCommands(viewModel);

            if (ar != null)
            {
                return ar;
            }

            if (!ModelState.IsValid)
            {
                return View(viewModel);
            }

            var key = await CreateKey(viewModel);

            TempData.SetStatusMessageModel(new StatusMessageModel()
            {
                Severity = StatusMessageModel.StatusSeverity.Success,
                Html = $"API key generated! <code>{key.Id}</code>"
            });
            return RedirectToAction("APIKeys");
        }
        private IActionResult HandleCommands(AddApiKeyViewModel viewModel)
        {
            switch (viewModel.Command)
            {
                case "change-store-mode":
                    viewModel.StoreMode = viewModel.StoreMode == AddApiKeyViewModel.ApiKeyStoreMode.Specific
                        ? AddApiKeyViewModel.ApiKeyStoreMode.AllStores
                        : AddApiKeyViewModel.ApiKeyStoreMode.Specific;

                    if (viewModel.StoreMode == AddApiKeyViewModel.ApiKeyStoreMode.Specific &&
                        !viewModel.SpecificStores.Any() && viewModel.Stores.Any())
                    {
                        viewModel.SpecificStores.Add(null);
                    }
                    return View(viewModel);
                case "add-store":
                    viewModel.SpecificStores.Add(null);
                    return View(viewModel);

                case string x when x.StartsWith("remove-store", StringComparison.InvariantCultureIgnoreCase):
                {
                    ModelState.Clear();
                    var index = int.Parse(
                        viewModel.Command.Substring(
                            viewModel.Command.IndexOf(":", StringComparison.InvariantCultureIgnoreCase) + 1),
                        CultureInfo.InvariantCulture);
                    viewModel.SpecificStores.RemoveAt(index);
                    return View(viewModel);
                }
            }

            return null;
        }

        private async Task<APIKeyData> CreateKey(AddApiKeyViewModel viewModel)
        {
            var key = new APIKeyData()
            {
                Id = Encoders.Hex.EncodeData(RandomUtils.GetBytes(20)),
                Type = APIKeyType.Permanent,
                UserId = _userManager.GetUserId(User),
                Label = viewModel.Label
            };
            key.SetPermissions(GetPermissionsFromViewModel(viewModel));
            await _apiKeyRepository.CreateKey(key);
            return key;
        }

        private IEnumerable<string> GetPermissionsFromViewModel(AddApiKeyViewModel viewModel)
        {
            var permissions = viewModel.PermissionValues.Where(tuple => tuple.Value).Select(tuple => tuple.Permission).ToList();

            if (viewModel.StoreMode == AddApiKeyViewModel.ApiKeyStoreMode.Specific)
            {
                permissions.AddRange(viewModel.SpecificStores.Select(Permissions.GetStorePermission));
            }
            else if (viewModel.StoreManagementPermission)
            {
                permissions.Add(Permissions.StoreManagement);
            }

            if (viewModel.IsServerAdmin && viewModel.ServerManagementPermission)
            {
                permissions.Add(Permissions.ServerManagement);
            }

            return permissions.Distinct();
        }

        private async Task<T> SetViewModelValues<T>(T viewModel) where T : AddApiKeyViewModel
        {
            viewModel.Stores = await _StoreRepository.GetStoresByUserId(_userManager.GetUserId(User));
            viewModel.IsServerAdmin =
                (await _authorizationService.AuthorizeAsync(User, Policies.CanModifyServerSettings.Key)).Succeeded;
            viewModel.PermissionValues ??= Permissions.GetAllPermissionKeys().Where(s =>
                    !s.Contains(Permissions.StoreManagement, StringComparison.InvariantCultureIgnoreCase) &&
                    s != Permissions.ServerManagement)
                .Select(s => new AddApiKeyViewModel.PermissionValueItem() {Permission = s, Value = true}).ToList();
            return viewModel;
        }

        public class AddApiKeyViewModel
        {
            public string Label { get; set; }
            public StoreData[] Stores { get; set; }
            public ApiKeyStoreMode StoreMode { get; set; }
            public List<string> SpecificStores { get; set; } = new List<string>();
            public bool IsServerAdmin { get; set; }
            public bool ServerManagementPermission { get; set; }
            public bool StoreManagementPermission { get; set; }
            public string Command { get; set; }
            public List<PermissionValueItem> PermissionValues { get; set; }

            public enum ApiKeyStoreMode
            {
                AllStores,
                Specific
            }

            public class PermissionValueItem
            {
                public string Permission { get; set; }
                public bool Value { get; set; }
            }
        }

        public class AuthorizeApiKeysViewModel : AddApiKeyViewModel
        {
            public string ApplicationName { get; set; }
            public bool Strict { get; set; }
            public bool SelectiveStores { get; set; }
            public string Permissions { get; set; }

            public string[] PermissionsFormatted
            {
                get
                {
                    return Permissions?.Split(";", StringSplitOptions.RemoveEmptyEntries)?? Array.Empty<string>();
                }
                set
                {
                    Permissions = string.Join(';', value ?? Array.Empty<string>());
                }
            }
        }


        public class ApiKeysViewModel
        {
            public List<APIKeyData> ApiKeyDatas { get; set; }
        }
    }
}
