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
                    UserId = new[] { _userManager.GetUserId(User) }
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

            var vm = await SetViewModelValues(new AuthorizeApiKeysViewModel(Permission.ToPermissions(permissions))
            {
                Label = applicationName,
                ApplicationName = applicationName,
                SelectiveStores = selectiveStores,
                Strict = strict,
            });
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

            if (viewModel.Strict)
            {
                for (int i = 0; i < viewModel.PermissionValues.Count; i++)
                {
                    if (viewModel.PermissionValues[i].Forbidden)
                    {
                        ModelState.AddModelError($"{viewModel.PermissionValues}[{i}].Value",
                                $"The permission '{viewModel.PermissionValues[i].Title}' is required for this application.");
                    }
                }
            }

            var permissions = Permission.ToPermissions(viewModel.Permissions).ToHashSet();
            if (permissions.Contains(Permission.Create(Policies.CanModifyStoreSettings)))
            {
                if (!viewModel.SelectiveStores &&
                    viewModel.StoreMode == AddApiKeyViewModel.ApiKeyStoreMode.Specific)
                {
                    viewModel.StoreMode = AddApiKeyViewModel.ApiKeyStoreMode.AllStores;
                    ModelState.AddModelError(nameof(viewModel.StoreManagementPermission),
                        "This application does not allow selective store permissions.");
                }

                if (!viewModel.StoreManagementPermission.Value && !viewModel.SpecificStores.Any() && viewModel.Strict)
                {
                    ModelState.AddModelError(nameof(viewModel.StoreManagementPermission),
                        $"This permission '{viewModel.StoreManagementPermission.Title}' is required for this application.");
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
                    return RedirectToAction("APIKeys", new { key = key.Id });
                default:
                    return View(viewModel);
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
            key.Permissions = string.Join(";", GetPermissionsFromViewModel(viewModel).Select(p => p.ToString()).Distinct().ToArray());
            await _apiKeyRepository.CreateKey(key);
            return key;
        }

        private IEnumerable<Permission> GetPermissionsFromViewModel(AddApiKeyViewModel viewModel)
        {
            List<Permission> permissions = new List<Permission>();
            foreach (var p in viewModel.PermissionValues.Where(tuple => tuple.Value && !tuple.Forbidden))
            {
                if (Permission.TryCreatePermission(p.Permission, null, out var pp))
                    permissions.Add(pp);
            }
            if (viewModel.StoreMode == AddApiKeyViewModel.ApiKeyStoreMode.AllStores && viewModel.StoreManagementPermission.Value)
            {
                permissions.Add(Permission.Create(Policies.CanModifyStoreSettings));
            }
            else if (viewModel.StoreMode == AddApiKeyViewModel.ApiKeyStoreMode.Specific)
            {
                permissions.AddRange(viewModel.SpecificStores.Select(s => Permission.Create(Policies.CanModifyStoreSettings, s)));
            }
            return permissions.Distinct();
        }

        private async Task<T> SetViewModelValues<T>(T viewModel) where T : AddApiKeyViewModel
        {
            viewModel.Stores = await _StoreRepository.GetStoresByUserId(_userManager.GetUserId(User));
            var isAdmin = (await _authorizationService.AuthorizeAsync(User, Policies.CanModifyServerSettings)).Succeeded;
            viewModel.PermissionValues ??= Policies.AllPolicies.Where(p => p != Policies.CanModifyStoreSettings)
                .Select(s => new AddApiKeyViewModel.PermissionValueItem() { Permission = s, Value = false }).ToList();
            if (!isAdmin)
            {
                foreach (var p in viewModel.PermissionValues)
                {
                    if (p.Permission == Policies.CanCreateUser ||
                        p.Permission == Policies.CanModifyServerSettings)
                    {
                        p.Forbidden = true;
                    }
                }
            }
            return viewModel;
        }

        public class AddApiKeyViewModel
        {
            public AddApiKeyViewModel()
            {
                StoreManagementPermission = new PermissionValueItem()
                {
                    Permission = Policies.CanModifyStoreSettings,
                    Value = false
                };
                StoreManagementSelectivePermission = new PermissionValueItem()
                {
                    Permission = $"{Policies.CanModifyStoreSettings}:",
                    Value = true
                };
            }
            public AddApiKeyViewModel(IEnumerable<Permission> permissions):this()
            {
                StoreManagementPermission.Value = permissions.Any(p => p.Policy == Policies.CanModifyStoreSettings && p.StoreId == null);
                PermissionValues = permissions.Where(p => p.Policy != Policies.CanModifyStoreSettings)
                                    .Select(p => new PermissionValueItem() { Permission = p.ToString(), Value = true })
                                    .ToList();
            }

            public IEnumerable<Permission> GetPermissions()
            {
                if (!(PermissionValues is null))
                {
                    foreach (var p in PermissionValues.Where(o => o.Value))
                    {
                        if (Permission.TryCreatePermission(p.Permission, null, out var pp))
                            yield return pp;
                    }
                }
                if (this.StoreMode == ApiKeyStoreMode.AllStores)
                {
                    if (StoreManagementPermission.Value)
                        yield return Permission.Create(Policies.CanModifyStoreSettings);
                }
                else if (this.StoreMode == ApiKeyStoreMode.Specific && SpecificStores is List<string>)
                {
                    foreach (var p in SpecificStores)
                    {
                        if (Permission.TryCreatePermission(Policies.CanModifyStoreSettings, p, out var pp))
                            yield return pp;
                    }
                }
            }
            public string Label { get; set; }
            public StoreData[] Stores { get; set; }
            public ApiKeyStoreMode StoreMode { get; set; }
            public List<string> SpecificStores { get; set; } = new List<string>();
            public PermissionValueItem StoreManagementPermission { get; set; }
            public PermissionValueItem StoreManagementSelectivePermission { get; set; }
            public string Command { get; set; }
            public List<PermissionValueItem> PermissionValues { get; set; }

            public enum ApiKeyStoreMode
            {
                AllStores,
                Specific
            }

            public class PermissionValueItem
            {
                public static readonly Dictionary<string, (string Title, string Description)> PermissionDescriptions = new Dictionary<string, (string Title, string Description)>()
                {
                    {BTCPayServer.Client.Policies.Unrestricted, ("Unrestricted access", "The app will have unrestricted access to your account.")},
                    {BTCPayServer.Client.Policies.CanCreateUser, ("Create new users", "The app will be able to create new users on this server.")},
                    {BTCPayServer.Client.Policies.CanModifyStoreSettings, ("Modify your stores", "The app will be able to create, view and modify, delete and create new invoices on the  all your stores.")},
                    {BTCPayServer.Client.Policies.CanViewStoreSettings, ("View your stores", "The app will be able to view stores settings.")},
                    {$"{BTCPayServer.Client.Policies.CanModifyStoreSettings}:", ("Manage selected stores", "The app will be able to view, modify, delete and create new invoices on the selected stores.")},
                    {BTCPayServer.Client.Policies.CanModifyServerSettings, ("Manage your server", "The app will have total control on the server settings of your server")},
                    {BTCPayServer.Client.Policies.CanViewProfile, ("View your profile", "The app will be able to view your user profile.")},
                    {BTCPayServer.Client.Policies.CanModifyProfile, ("Manage your profile", "The app will be able to view and modify your user profile.")},
                    {BTCPayServer.Client.Policies.CanCreateInvoice, ("Create an invoice", "The app will be able to create new invoice.")},
                };
                public string Title
                {
                    get
                    {
                        return PermissionDescriptions[Permission].Title;
                    }
                }
                public string Description
                {
                    get
                    {
                        return PermissionDescriptions[Permission].Description;
                    }
                }
                public string Permission { get; set; }
                public bool Value { get; set; }
                public bool Forbidden { get; set; }
            }
        }

        public class AuthorizeApiKeysViewModel : AddApiKeyViewModel
        {
            public AuthorizeApiKeysViewModel()
            {

            }
            public AuthorizeApiKeysViewModel(IEnumerable<Permission> permissions) : base(permissions)
            {
                Permissions = string.Join(';', permissions.Select(p => p.ToString()).ToArray());
            }
            public string ApplicationName { get; set; }
            public bool Strict { get; set; }
            public bool SelectiveStores { get; set; }
            public string Permissions { get; set; }
        }


        public class ApiKeysViewModel
        {
            public List<APIKeyData> ApiKeyDatas { get; set; }
        }
    }
}
