using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Models;
using BTCPayServer.Security.GreenField;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;
using NBitcoin.DataEncoders;

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
                Title = $"Delete API Key {(string.IsNullOrEmpty(key.Label) ? string.Empty : key.Label)}",
                DescriptionHtml = true,
                Description = $"Any application using this API key will immediately lose access: <code>{key.Id}</code>",
                Action = "Delete",
                ActionUrl = Url.ActionLink(nameof(RemoveAPIKeyPost), values: new { id })
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
        public async Task<IActionResult> AuthorizeAPIKey(string[] permissions, string applicationName = null, Uri redirect = null,
            bool strict = true, bool selectiveStores = false, string applicationIdentifier = null)
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

            var requestPermissions = Permission.ToPermissions(permissions);
            if (!string.IsNullOrEmpty(applicationIdentifier) && redirect != null)
            {
                //check if there is an app identifier that matches and belongs to the current user
                var keys = await _apiKeyRepository.GetKeys(new APIKeyRepository.APIKeyQuery()
                {
                    UserId = new[] {_userManager.GetUserId(User)}
                });
                foreach (var key in keys)
                {
                    var blob = key.GetBlob();

                    if (blob.ApplicationIdentifier != applicationIdentifier ||
                        blob.ApplicationAuthority != redirect.Authority)
                    {
                        continue;
                    }

                    //matched the identifier and authority, but we need to check if what the app is requesting in terms of permissions is enough
                    var alreadyPresentPermissions = Permission.ToPermissions(blob.Permissions)
                        .GroupBy(permission => permission.Policy);
                    var fail = false;
                    foreach (var permission in requestPermissions.GroupBy(permission => permission.Policy))
                    {
                        var presentPermission =
                            alreadyPresentPermissions.SingleOrDefault(grouping => permission.Key == grouping.Key);
                        if (strict && presentPermission == null)
                        {
                            fail = true;
                            break;
                        }

                        if (Policies.IsStorePolicy(permission.Key))
                        {
                            if (!selectiveStores &&
                                permission.Any(permission1 => !string.IsNullOrEmpty(permission1.Scope)))
                            {

                                TempData.SetStatusMessageModel(new StatusMessageModel()
                                {
                                    Severity = StatusMessageModel.StatusSeverity.Error,
                                    Message =
                                        "Cannot request specific store permission when selectiveStores is not enable"
                                });
                                return RedirectToAction("APIKeys");
                            }
                            else if (!selectiveStores && presentPermission.Any(permission1 =>
                                !string.IsNullOrEmpty(permission1.Scope)))
                            {
                                fail = true;
                                break;
                            }
                        }
                    }

                    if (fail)
                    {
                        continue;
                    }

                    //we have a key that is sufficient, redirect to a page to confirm that it's ok to provide this key to the app.
                    return View("ConfirmAPIKey",
                        new AuthorizeApiKeysViewModel()
                        {
                            ApiKey = key.Id,
                            RedirectUrl = redirect,
                            Label = applicationName,
                            ApplicationName = applicationName,
                            SelectiveStores = selectiveStores,
                            Strict = strict,
                            Permissions = string.Join(';', permissions),
                            ApplicationIdentifier = applicationIdentifier
                        });
                }
            }

            var vm = await SetViewModelValues(new AuthorizeApiKeysViewModel()
            {
                RedirectUrl = redirect,
                Label = applicationName,
                ApplicationName = applicationName,
                SelectiveStores = selectiveStores,
                Strict = strict,
                Permissions = string.Join(';', requestPermissions),
                ApplicationIdentifier = applicationIdentifier
            });
            AdjustVMForAuthorization(vm);

            return View(vm);
        }

        private void AdjustVMForAuthorization(AuthorizeApiKeysViewModel vm)
        {
            var parsedPermissions = Permission.ToPermissions(vm.Permissions.Split(';')).GroupBy(permission => permission.Policy);

            for (var index = vm.PermissionValues.Count - 1; index >= 0; index--)
            {
                var permissionValue = vm.PermissionValues[index];
                var wanted = parsedPermissions?.SingleOrDefault(permission =>
                    permission.Key.Equals(permissionValue.Permission,
                        StringComparison.InvariantCultureIgnoreCase));
                if (vm.Strict && !(wanted?.Any() ?? false))
                {
                    vm.PermissionValues.RemoveAt(index);
                    continue;
                }
                else if (wanted?.Any() ?? false)
                {
                    if (vm.SelectiveStores && Policies.IsStorePolicy(permissionValue.Permission) &&
                        wanted.Any(permission => !string.IsNullOrEmpty(permission.Scope)))
                    {
                        permissionValue.StoreMode = AddApiKeyViewModel.ApiKeyStoreMode.Specific;
                        permissionValue.SpecificStores = wanted.Select(permission => permission.Scope).ToList();
                    }
                    else
                    {
                        permissionValue.StoreMode = AddApiKeyViewModel.ApiKeyStoreMode.AllStores;
                        permissionValue.SpecificStores = new List<string>();
                        permissionValue.Value = true;
                    }
                }
            }
        }

        [HttpPost("~/api-keys/authorize")]
        public async Task<IActionResult> AuthorizeAPIKey([FromForm] AuthorizeApiKeysViewModel viewModel)
        {
            await SetViewModelValues(viewModel);

            AdjustVMForAuthorization(viewModel);
            var ar = HandleCommands(viewModel);

            if (ar != null)
            {
                return ar;
            }

            for (int i = 0; i < viewModel.PermissionValues.Count; i++)
            {
                if (viewModel.PermissionValues[i].Forbidden && viewModel.Strict)
                {
                    viewModel.PermissionValues[i].Value = false;
                    ModelState.AddModelError($"{viewModel.PermissionValues}[{i}].Value",
                        $"The permission '{viewModel.PermissionValues[i].Title}' is required for this application.");
                }

                if (viewModel.PermissionValues[i].StoreMode == AddApiKeyViewModel.ApiKeyStoreMode.Specific &&
                    !viewModel.SelectiveStores)
                {
                    viewModel.PermissionValues[i].StoreMode = AddApiKeyViewModel.ApiKeyStoreMode.AllStores;
                    ModelState.AddModelError($"{viewModel.PermissionValues}[{i}].Value",
                        $"The permission '{viewModel.PermissionValues[i].Title}' cannot be store specific for this application.");
                }
            }

            if (!ModelState.IsValid)
            {
                return View(viewModel);
            }

            var command = viewModel.Command.ToLowerInvariant();
            switch (command)
            {
                case "cancel":
                    return RedirectToAction("APIKeys");

                case "authorize":
                case "confirm":
                    var key = command == "authorize"
                        ? await CreateKey(viewModel, (viewModel.ApplicationIdentifier, viewModel.RedirectUrl?.Authority))
                        : await _apiKeyRepository.GetKey(viewModel.ApiKey);
    
                    if (viewModel.RedirectUrl != null)
                    {
                        var permissions = key.GetBlob().Permissions;
                        var redirectVm = new PostRedirectViewModel()
                        {
                            FormUrl = viewModel.RedirectUrl.ToString(),
                            Parameters =
                            {
                                new KeyValuePair<string, string>("apiKey", key.Id),
                                new KeyValuePair<string, string>("userId", key.UserId)
                            }
                        };
                        foreach (var permission in permissions)
                        {
                            redirectVm.Parameters.Add(
                                new KeyValuePair<string, string>("permissions[]", permission));
                        }

                        return View("PostRedirect", redirectVm);
                    }

                    TempData.SetStatusMessageModel(new StatusMessageModel()
                    {
                        Severity = StatusMessageModel.StatusSeverity.Success,
                        Html = $"API key generated! <code class='alert-link'>{key.Id}</code>"
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
                Html = $"API key generated! <code class='alert-link'>{key.Id}</code>"
            });
            return RedirectToAction("APIKeys");
        }

        private IActionResult HandleCommands(AddApiKeyViewModel viewModel)
        {
            if (string.IsNullOrEmpty(viewModel.Command))
            {
                return null;
            }
            var parts = viewModel.Command.Split(':', StringSplitOptions.RemoveEmptyEntries);
            var permission = parts[0];
            if (!Policies.IsStorePolicy(permission))
            {
                return null;
            }
            var permissionValueItem = viewModel.PermissionValues.Single(item => item.Permission == permission);
            var command = parts[1];
            var storeIndex = parts.Length == 3 ? parts[2] : null;

            ModelState.Clear();
            switch (command)
            {
                case "change-store-mode":
                    permissionValueItem.StoreMode = permissionValueItem.StoreMode == AddApiKeyViewModel.ApiKeyStoreMode.Specific
                        ? AddApiKeyViewModel.ApiKeyStoreMode.AllStores
                        : AddApiKeyViewModel.ApiKeyStoreMode.Specific;

                    if (permissionValueItem.StoreMode == AddApiKeyViewModel.ApiKeyStoreMode.Specific &&
                        !permissionValueItem.SpecificStores.Any() && viewModel.Stores.Any())
                    {
                        permissionValueItem.SpecificStores.Add(null);
                    }
                    return View(viewModel);

                case "add-store":
                    permissionValueItem.SpecificStores.Add(null);
                    return View(viewModel);

                case "remove-store":
                    {
                        if (storeIndex != null)
                            permissionValueItem.SpecificStores.RemoveAt(int.Parse(storeIndex,
                                CultureInfo.InvariantCulture));
                        return View(viewModel);
                    }
            }

            return null;
        }

        private async Task<APIKeyData> CreateKey(AddApiKeyViewModel viewModel, (string appIdentifier, string appAuthority) app = default)
        {
            var key = new APIKeyData()
            {
                Id = Encoders.Hex.EncodeData(RandomUtils.GetBytes(20)),
                Type = APIKeyType.Permanent,
                UserId = _userManager.GetUserId(User),
                Label = viewModel.Label,
            };
            key.SetBlob(new APIKeyBlob()
            {
                Permissions = GetPermissionsFromViewModel(viewModel).Select(p => p.ToString()).Distinct().ToArray(),
                ApplicationAuthority = app.appAuthority,
                ApplicationIdentifier = app.appIdentifier
            });
            await _apiKeyRepository.CreateKey(key);
            return key;
        }

        private IEnumerable<Permission> GetPermissionsFromViewModel(AddApiKeyViewModel viewModel)
        {
            List<Permission> permissions = new List<Permission>();
            foreach (var p in viewModel.PermissionValues.Where(tuple => !tuple.Forbidden))
            {
                if (Policies.IsStorePolicy(p.Permission))
                {
                    if (p.StoreMode == AddApiKeyViewModel.ApiKeyStoreMode.AllStores && p.Value)
                    {
                        permissions.Add(Permission.Create(p.Permission));
                    }
                    else if (p.StoreMode == AddApiKeyViewModel.ApiKeyStoreMode.Specific)
                    {
                        permissions.AddRange(p.SpecificStores.Select(s => Permission.Create(p.Permission, s)));
                    }
                }
                else if (p.Value && Permission.TryCreatePermission(p.Permission, null, out var pp))
                    permissions.Add(pp);
            }


            return permissions.Distinct();
        }

        private async Task<T> SetViewModelValues<T>(T viewModel) where T : AddApiKeyViewModel
        {
            viewModel.Stores = await _StoreRepository.GetStoresByUserId(_userManager.GetUserId(User));
            var isAdmin = (await _authorizationService.AuthorizeAsync(User, Policies.CanModifyServerSettings))
                .Succeeded;
            viewModel.PermissionValues ??= Policies.AllPolicies
                .Where(p => AddApiKeyViewModel.PermissionValueItem.PermissionDescriptions.ContainsKey(p))
                .Select(s => new AddApiKeyViewModel.PermissionValueItem()
                {
                    Permission = s,
                    Value = false,
                    Forbidden = Policies.IsServerPolicy(s) && !isAdmin
                }).ToList();


            if (!isAdmin)
            {
                foreach (var p in viewModel.PermissionValues.Where(item => Policies.IsServerPolicy(item.Permission)))
                {
                    p.Forbidden = true;
                }
            }

            return viewModel;
        }

        public class AddApiKeyViewModel
        {
            public string Label { get; set; }
            public StoreData[] Stores { get; set; }
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
                    {BTCPayServer.Client.Policies.CanModifyStoreSettings, ("Modify your stores", "The app will be able to view, modify, delete and create new invoices on all your stores.")},
                    {$"{BTCPayServer.Client.Policies.CanModifyStoreSettings}:", ("Manage selected stores", "The app will be able to view, modify, delete and create new invoices on the selected stores.")},
                    {BTCPayServer.Client.Policies.CanModifyStoreWebhooks, ("Modify stores webhooks", "The app will be mofidy the webhooks of all your stores.")},
                    {$"{BTCPayServer.Client.Policies.CanModifyStoreWebhooks}:", ("Modify selected stores' webhooks", "The app will be mofidy the webhooks of the selected stores.")},
                    {BTCPayServer.Client.Policies.CanViewStoreSettings, ("View your stores", "The app will be able to view stores settings.")},
                    {$"{BTCPayServer.Client.Policies.CanViewStoreSettings}:", ("View your stores", "The app will be able to view the selected stores' settings.")},
                    {BTCPayServer.Client.Policies.CanModifyServerSettings, ("Manage your server", "The app will have total control on the server settings of your server")},
                    {BTCPayServer.Client.Policies.CanViewProfile, ("View your profile", "The app will be able to view your user profile.")},
                    {BTCPayServer.Client.Policies.CanModifyProfile, ("Manage your profile", "The app will be able to view and modify your user profile.")},
                    {BTCPayServer.Client.Policies.CanCreateInvoice, ("Create an invoice", "The app will be able to create new invoices.")},
                    {$"{BTCPayServer.Client.Policies.CanCreateInvoice}:", ("Create an invoice", "The app will be able to create new invoices on the selected stores.")},
                    {BTCPayServer.Client.Policies.CanViewInvoices, ("View invoices", "The app will be able to view invoices.")},
                    {$"{BTCPayServer.Client.Policies.CanViewInvoices}:", ("View invoices", "The app will be able to view invoices on the selected stores.")},
                    {BTCPayServer.Client.Policies.CanModifyPaymentRequests, ("Modify your payment requests", "The app will be able to view, modify, delete and create new payment requests on all your stores.")},
                    {$"{BTCPayServer.Client.Policies.CanModifyPaymentRequests}:", ("Manage selected stores' payment requests", "The app will be able to view, modify, delete and create new payment requests on the selected stores.")},
                    {BTCPayServer.Client.Policies.CanViewPaymentRequests, ("View your payment requests", "The app will be able to view payment requests.")},
                    {$"{BTCPayServer.Client.Policies.CanViewPaymentRequests}:", ("View your payment requests", "The app will be able to view the selected stores' payment requests.")},
                    {BTCPayServer.Client.Policies.CanUseInternalLightningNode, ("Use the internal lightning node", "The app will be able to  use the internal BTCPay Server lightning node to create BOLT11 invoices, connect to other nodes, open new channels and pay BOLT11 invoices.")},
                    {BTCPayServer.Client.Policies.CanCreateLightningInvoiceInternalNode, ("Create invoices with internal lightning node", "The app will be able to use the internal BTCPay Server lightning node to create BOLT11 invoices.")},
                    {BTCPayServer.Client.Policies.CanUseLightningNodeInStore, ("Use the lightning nodes associated with your stores", "The app will be able to use the lightning nodes connected to all your stores to create BOLT11 invoices, connect to other nodes, open new channels and pay BOLT11 invoices.")},
                    {BTCPayServer.Client.Policies.CanCreateLightningInvoiceInStore, ("Create invoices the lightning nodes associated with your stores", "The app will be able to use the lightning nodes connected to all your stores to create BOLT11 invoices.")},
                    {$"{BTCPayServer.Client.Policies.CanUseLightningNodeInStore}:", ("Use the lightning nodes associated with your stores", "The app will be able to use the lightning nodes connected to the selected stores to create BOLT11 invoices, connect to other nodes, open new channels and pay BOLT11 invoices.")},
                    {$"{BTCPayServer.Client.Policies.CanCreateLightningInvoiceInStore}:", ("Create invoices the lightning nodes associated with your stores", "The app will be able to use the lightning nodes connected to the selected stores to create BOLT11 invoices.")},
                };
                public string Title
                {
                    get
                    {
                        return PermissionDescriptions[$"{Permission}{(StoreMode == ApiKeyStoreMode.Specific ? ":" : "")}"].Title;
                    }
                }
                public string Description
                {
                    get
                    {
                        return PermissionDescriptions[$"{Permission}{(StoreMode == ApiKeyStoreMode.Specific ? ":" : "")}"].Description;
                    }
                }
                public string Permission { get; set; }
                public bool Value { get; set; }
                public bool Forbidden { get; set; }

                public ApiKeyStoreMode StoreMode { get; set; } = ApiKeyStoreMode.AllStores;
                public List<string> SpecificStores { get; set; } = new List<string>();
            }
        }

        public class AuthorizeApiKeysViewModel : AddApiKeyViewModel
        {
            public string ApplicationName { get; set; }
            public string ApplicationIdentifier { get; set; }
            public Uri RedirectUrl { get; set; }
            public bool Strict { get; set; }
            public bool SelectiveStores { get; set; }
            public string Permissions { get; set; }
            public string ApiKey { get; set; }
        }

        public class ApiKeysViewModel
        {
            public List<APIKeyData> ApiKeyDatas { get; set; }
        }
    }
}
