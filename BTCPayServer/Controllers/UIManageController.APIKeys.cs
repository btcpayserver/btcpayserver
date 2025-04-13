using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Abstractions.Services;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Models;
using BTCPayServer.Security.Greenfield;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;
using NBitcoin.DataEncoders;

namespace BTCPayServer.Controllers
{
    public partial class UIManageController
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

        [HttpGet("~/api-keys/{id}/delete")]
        public async Task<IActionResult> DeleteAPIKey(string id)
        {
            var key = await _apiKeyRepository.GetKey(id);
            if (key == null || key.UserId != _userManager.GetUserId(User))
            {
                return NotFound();
            }
            return View("Confirm", new ConfirmModel
            {
                Title = "Delete API key",
                Description = $"Any application using the API key <strong>{Html.Encode(key.Label ?? key.Id)}<strong> will immediately lose access.",
                Action = "Delete",
                ActionName = nameof(DeleteAPIKeyPost)
            });
        }

        [HttpPost("~/api-keys/{id}/delete")]
        public async Task<IActionResult> DeleteAPIKeyPost(string id)
        {
            var key = await _apiKeyRepository.GetKey(id);
            if (key == null || key.UserId != _userManager.GetUserId(User))
            {
                return NotFound();
            }
            await _apiKeyRepository.Remove(id, _userManager.GetUserId(User));
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Severity = StatusMessageModel.StatusSeverity.Success,
                Message = StringLocalizer["API Key removed"].Value
            });
            return RedirectToAction("APIKeys");
        }

        [HttpGet]
        public async Task<IActionResult> AddApiKey()
        {
            if (!_btcPayServerEnvironment.IsSecure(HttpContext))
            {
                TempData.SetStatusMessageModel(new StatusMessageModel
                {
                    Severity = StatusMessageModel.StatusSeverity.Error,
                    Message = StringLocalizer["Cannot generate API keys while not using HTTPS or Tor"].Value
                });
                return RedirectToAction("APIKeys");
            }

            return View("AddApiKey", await SetViewModelValues(new AddApiKeyViewModel()));
        }

        [HttpGet("~/api-keys/authorize")]
        public async Task<IActionResult> AuthorizeAPIKey(string[] permissions, string applicationName = null, Uri redirect = null,
            bool strict = true, bool selectiveStores = false, string applicationIdentifier = null)
        {
            if (!_btcPayServerEnvironment.IsSecure(HttpContext))
            {
                TempData.SetStatusMessageModel(new StatusMessageModel
                {
                    Severity = StatusMessageModel.StatusSeverity.Error,
                    Message = StringLocalizer["Cannot generate API keys while not using HTTPS or Tor"].Value
                });
                return RedirectToAction("APIKeys");
            }

            permissions ??= Array.Empty<string>();

            var requestPermissions = Permission.ToPermissions(permissions).ToList();

            if (redirect?.IsAbsoluteUri is false)
            {
                redirect = null;
            }

            var vm = new AuthorizeApiKeysViewModel
            {
                RedirectUrl = redirect,
                Label = applicationName,
                ApplicationName = applicationName,
                SelectiveStores = selectiveStores,
                Strict = strict,
                Permissions = string.Join(';', requestPermissions),
                ApplicationIdentifier = applicationIdentifier
            };

            var existingApiKey = await CheckForMatchingApiKey(requestPermissions, vm);
            if (existingApiKey != null)
            {
                vm.ApiKey = existingApiKey.Id;
                return View("ConfirmAPIKey", vm);
            }

            vm = await SetViewModelValues(vm);
            AdjustVMForAuthorization(vm);

            return View(vm);
        }

        [HttpPost("~/api-keys/authorize")]
        public async Task<IActionResult> AuthorizeAPIKey([FromForm] AuthorizeApiKeysViewModel viewModel)
        {
            viewModel = await SetViewModelValues(viewModel);
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
                        ? await CreateKey(viewModel, (viewModel.ApplicationIdentifier, viewModel.RedirectUrl?.AbsoluteUri))
                        : await _apiKeyRepository.GetKey(viewModel.ApiKey);

                    if (viewModel.RedirectUrl != null)
                    {
                        var permissions = key.GetBlob().Permissions;
                        var redirectVm = new PostRedirectViewModel()
                        {
                            AllowExternal = true,
                            FormUrl = viewModel.RedirectUrl.AbsoluteUri,
                            FormParameters =
                            {
                                { "apiKey", key.Id },
                                { "userId", key.UserId },
                            },
                        };
                        foreach (var permission in permissions)
                        {
                            redirectVm.FormParameters.Add("permissions[]", permission);
                        }
                        return View("PostRedirect", redirectVm);
                    }

                    TempData.SetStatusMessageModel(new StatusMessageModel
                    {
                        Severity = StatusMessageModel.StatusSeverity.Success,
                        Html = StringLocalizer["API key generated!"].Value + $" <code class='alert-link'>{key.Id}</code>"
                    });

                    return RedirectToAction("APIKeys", new { key = key.Id });

                default:
                    var perms = viewModel.Permissions?.Split(';').ToArray() ?? Array.Empty<string>();
                    if (perms.Any())
                    {
                        var requestPermissions = Permission.ToPermissions(perms).ToList();
                        var existingApiKey = await CheckForMatchingApiKey(requestPermissions, viewModel);
                        if (existingApiKey != null)
                        {
                            viewModel.ApiKey = existingApiKey.Id;
                            return View("ConfirmAPIKey", viewModel);
                        }
                    }
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

            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Severity = StatusMessageModel.StatusSeverity.Success,
                Html = StringLocalizer["API key generated!"].Value + $" <code class='alert-link'>{key.Id}</code>"
            });
            return RedirectToAction("APIKeys");
        }

        private async Task<APIKeyData> CheckForMatchingApiKey(IEnumerable<Permission> requestedPermissions, AuthorizeApiKeysViewModel vm)
        {
            if (string.IsNullOrEmpty(vm.ApplicationIdentifier) || vm.RedirectUrl == null)
            {
                return null;
            }

            //check if there is an app identifier that matches and belongs to the current user
            var keys = await _apiKeyRepository.GetKeys(new APIKeyRepository.APIKeyQuery
            {
                UserId = new[] { _userManager.GetUserId(User) }
            });
            foreach (var key in keys)
            {
                var blob = key.GetBlob();
                if (blob.ApplicationIdentifier != vm.ApplicationIdentifier || blob.ApplicationAuthority != vm.RedirectUrl.AbsoluteUri)
                {
                    continue;
                }

                var requestedGrouped = requestedPermissions.GroupBy(permission => permission.Policy);
                var existingGrouped = Permission.ToPermissions(blob.Permissions).GroupBy(permission => permission.Policy);

                //matched the identifier and authority, but we need to check if what the app is requesting in terms of permissions is enough
                var fail = false;
                foreach (var requested in requestedGrouped)
                {
                    var existing = existingGrouped.SingleOrDefault(grouping => requested.Key == grouping.Key);
                    if (vm.Strict && existing == null)
                    {
                        fail = true;
                        break;
                    }

                    if (Policies.IsStorePolicy(requested.Key))
                    {
                        if ((vm.SelectiveStores && !existing.Any(p => p.Scope == vm.StoreId)) ||
                            (!vm.SelectiveStores && existing.Any(p => !string.IsNullOrEmpty(p.Scope))))
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
                return key;
            }

            return null;
        }

        private void AdjustVMForAuthorization(AuthorizeApiKeysViewModel vm)
        {
            var permissions = vm.Permissions?.Split(';') ?? Array.Empty<string>();
            var permissionsWithStoreIDs = new List<string>();

            vm.NeedsStorePermission = vm.SelectiveStores && (permissions.Any(Policies.IsStorePolicy) || !vm.Strict);

            // Go over each permission and associated store IDs and join them
            // so that permission for a specific store is parsed correctly
            foreach (var permission in permissions)
            {
                if (!Policies.IsStorePolicy(permission) || string.IsNullOrEmpty(vm.StoreId))
                {
                    permissionsWithStoreIDs.Add(permission);
                }
                else
                {
                    permissionsWithStoreIDs.Add($"{permission}:{vm.StoreId}");
                }
            }

            var parsedPermissions = Permission.ToPermissions(permissionsWithStoreIDs.ToArray()).GroupBy(permission => permission.Policy);

            for (var index = vm.PermissionValues.Count - 1; index >= 0; index--)
            {
                var permissionValue = vm.PermissionValues[index];
                var wanted = parsedPermissions.SingleOrDefault(permission =>
                    permission.Key.Equals(permissionValue.Permission,
                        StringComparison.InvariantCultureIgnoreCase));
                if (vm.Strict && !(wanted?.Any() ?? false))
                {
                    vm.PermissionValues.RemoveAt(index);
                    continue;
                }
                if (wanted?.Any() ?? false)
                {
                    var commandParts = vm.Command?.Split(':', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
                    var command = commandParts.Length > 1 ? commandParts[1] : null;
                    var isPerformingAnAction = command == "change-store-mode" || command == "add-store";

                    // Don't want to accidentally change mode for the user if they are explicitly performing some action
                    if (isPerformingAnAction)
                    {
                        continue;
                    }

                    // Set the value to true and adjust the other fields based on the policy type
                    permissionValue.Value = true;

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
                    }
                }
            }
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
                    // Reset values for "all stores" option to their original values
                    if (permissionValueItem.StoreMode == AddApiKeyViewModel.ApiKeyStoreMode.AllStores)
                    {
                        permissionValueItem.SpecificStores = new List<string>();
                        permissionValueItem.Value = true;
                    }

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
            var key = new APIKeyData
            {
                Id = Encoders.Hex.EncodeData(RandomUtils.GetBytes(20)),
                Type = APIKeyType.Permanent,
                UserId = _userManager.GetUserId(User),
                Label = viewModel.Label,
            };
            key.SetBlob(new APIKeyBlob
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
            var permissions = new List<Permission>();
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
                foreach (var p in viewModel.PermissionValues.Where(item => item.Permission is null || Policies.IsServerPolicy(item.Permission)))
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
                    {Policies.Unrestricted, ("Unrestricted access", "Grants unrestricted access to your account.")},
                    {Policies.CanViewUsers, ("View users", "Allows seeing all users on this server.")},
                    {Policies.CanCreateUser, ("Create new users", "Allows creating new users on this server.")},
                    {Policies.CanManageUsers, ("Manage users", "Allows creating/deleting API keys for users.")},
                    {Policies.CanDeleteUser, ("Delete user", "Allows deleting the user to whom it is assigned. Admin users can delete any user without this permission.")},
                    {Policies.CanModifyStoreSettings, ("Modify your stores", "Allows managing invoices on all your stores and modify their settings.")},
                    {$"{Policies.CanModifyStoreSettings}:", ("Manage selected stores", "Allows managing invoices on the selected stores and modify their settings.")},
                    {Policies.CanModifyWebhooks, ("Modify stores webhooks", "Allows modifying the webhooks of all your stores.")},
                    {$"{Policies.CanModifyWebhooks}:", ("Modify selected stores' webhooks", "Allows modifying the webhooks of the selected stores.")},
                    {Policies.CanViewStoreSettings, ("View your stores", "Allows viewing stores settings.")},
                    {$"{Policies.CanViewStoreSettings}:", ("View your stores", "Allows viewing the selected stores' settings.")},
                    {Policies.CanViewReports, ("View your reports", "Allows viewing reports.")},
                    {$"{Policies.CanViewReports}:", ("View your selected stores' reports", "Allows viewing the selected stores' reports.")},
                    {Policies.CanModifyServerSettings, ("Manage your server", "Grants total control on the server settings of your server.")},
                    {Policies.CanViewProfile, ("View your profile", "Allows viewing your user profile.")},
                    {Policies.CanModifyProfile, ("Manage your profile", "Allows viewing and modifying your user profile.")},
                    {Policies.CanManageNotificationsForUser, ("Manage your notifications", "Allows viewing and modifying your user notifications.")},
                    {Policies.CanViewNotificationsForUser, ("View your notifications", "Allows viewing your user notifications.")},
                    {Policies.CanCreateInvoice, ("Create an invoice", "Allows creating new invoices.")},
                    {$"{Policies.CanCreateInvoice}:", ("Create an invoice", "Allows creating new invoices on the selected stores.")},
                    {Policies.CanViewInvoices, ("View invoices", "Allows viewing invoices.")},
                    {Policies.CanModifyInvoices, ("Modify invoices", "Allows viewing and modifying invoices.")},
                    {$"{Policies.CanViewInvoices}:", ("View invoices", "Allows viewing invoices on the selected stores.")},
                    {$"{Policies.CanModifyInvoices}:", ("Modify invoices", "Allows viewing and modifying invoices on the selected stores.")},
                    {Policies.CanModifyPaymentRequests, ("Modify your payment requests", "Allows viewing, modifying, deleting and creating new payment requests on all your stores.")},
                    {$"{Policies.CanModifyPaymentRequests}:", ("Manage selected stores' payment requests", "Allows viewing, modifying, deleting and creating new payment requests on the selected stores.")},
                    {Policies.CanViewPaymentRequests, ("View your payment requests", "Allows viewing payment requests.")},
                    {$"{Policies.CanViewPaymentRequests}:", ("View your payment requests", "Allows viewing the selected stores' payment requests.")},
                    {Policies.CanViewPullPayments, ("View your pull payments", "Allows viewing pull payments on all your stores.")},
                    {$"{Policies.CanViewPullPayments}:", ("View selected stores' pull payments", "Allows viewing pull payments on the selected stores.")},
                    {Policies.CanManagePullPayments, ("Manage your pull payments", "Allows viewing, modifying, deleting and creating pull payments on all your stores.")},
                    {$"{Policies.CanManagePullPayments}:", ("Manage selected stores' pull payments", "Allows viewing, modifying, deleting and creating pull payments on the selected stores.")},
                    {Policies.CanArchivePullPayments, ("Archive your pull payments", "Allows deleting pull payments on all your stores.")},
                    {$"{Policies.CanArchivePullPayments}:", ("Archive selected stores' pull payments", "Allows deleting pull payments on the selected stores.")},
                    {Policies.CanCreatePullPayments, ("Create pull payments", "Allows creating pull payments on all your stores.")},
                    {$"{Policies.CanCreatePullPayments}:", ("Create pull payments in selected stores", "Allows creating pull payments on the selected stores.")},
                    {Policies.CanManagePayouts, ("Manage payouts", "Allows managing payouts on all your stores.")},
                    {$"{Policies.CanManagePayouts}:", ("Manage payouts in selected stores", "Allows managing payouts on the selected stores.")},
                    {Policies.CanViewPayouts, ("View payouts", "Allows viewing payouts on all your stores.")},
                    {$"{Policies.CanViewPayouts}:", ("View payouts in selected stores", "Allows viewing payouts on the selected stores.")},
                    {Policies.CanCreateNonApprovedPullPayments, ("Create non-approved pull payments", "Allows creating pull payments without automatic approval on all your stores.")},
                    {$"{Policies.CanCreateNonApprovedPullPayments}:", ("Create non-approved pull payments in selected stores", "Allows viewing, modifying, deleting and creating pull payments without automatic approval on the selected stores.")},
                    {Policies.CanUseInternalLightningNode, ("Use the internal lightning node", "Allows using the internal BTCPay Server lightning node to create BOLT11 invoices, connect to other nodes, open new channels and pay BOLT11 invoices.")},
                    {Policies.CanViewLightningInvoiceInternalNode, ("View invoices from internal lightning node", "Allows using the internal BTCPay Server lightning node to view BOLT11 invoices.")},
                    {Policies.CanCreateLightningInvoiceInternalNode, ("Create invoices with internal lightning node", "Allows using the internal BTCPay Server lightning node to create BOLT11 invoices.")},
                    {Policies.CanUseLightningNodeInStore, ("Use the lightning nodes associated with your stores", "Allows using the lightning nodes connected to all your stores to create BOLT11 invoices, connect to other nodes, open new channels and pay BOLT11 invoices.")},
                    {Policies.CanViewLightningInvoiceInStore, ("View the lightning invoices associated with your stores", "Allows viewing the lightning invoices connected to all your stores.")},
                    {Policies.CanCreateLightningInvoiceInStore, ("Create invoices from the lightning nodes associated with your stores", "Allows using the lightning nodes connected to all your stores to create BOLT11 invoices.")},
                    {$"{Policies.CanUseLightningNodeInStore}:", ("Use the lightning nodes associated with your stores", "Allows using the lightning nodes connected to the selected stores to create BOLT11 invoices, connect to other nodes, open new channels and pay BOLT11 invoices.")},
                    {$"{Policies.CanViewLightningInvoiceInStore}:", ("View the lightning invoices associated with your stores", "Allows viewing the lightning invoices connected to the selected stores.")},
                    {$"{Policies.CanCreateLightningInvoiceInStore}:", ("Create invoices from the lightning nodes associated with your stores", "Allows using the lightning nodes connected to the selected stores to create BOLT11 invoices.")},
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
                public List<string> SpecificStores { get; set; } = new();
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
            public bool NeedsStorePermission { get; set; }
            public string StoreId { get; set; }
        }

        public class ApiKeysViewModel
        {
            public List<APIKeyData> ApiKeyDatas { get; set; }
        }
    }
}
