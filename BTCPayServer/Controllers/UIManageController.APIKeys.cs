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
using BTCPayServer.Security.Greenfield;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
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
                    UserId = new[] { User.GetId() }
                })
            });
        }

        [HttpGet("~/api-keys/{id}/delete")]
        public async Task<IActionResult> DeleteAPIKey(string id)
        {
            var key = await _apiKeyRepository.GetKey(id);
            if (key == null || key.UserId != User.GetId())
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
            if (key == null || key.UserId != User.GetId())
            {
                return NotFound();
            }
            await _apiKeyRepository.Remove(id, User.GetId());
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

            var requestPermissions = Permission.ToPermissions(permissions)
                .Where(permission => _permissionService.IsValidPolicy(permission.Policy))
                .ToList();

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
                        var requestPermissions = Permission.ToPermissions(perms)
                            .Where(permission => _permissionService.IsValidPolicy(permission.Policy))
                            .ToList();
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
                UserId = new[] { User.GetId() }
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

                    if (IsStorePolicy(requested.Key))
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

            vm.NeedsStorePermission = vm.SelectiveStores && (permissions.Any(IsStorePolicy) || !vm.Strict);

            // Go over each permission and associated store IDs and join them
            // so that permission for a specific store is parsed correctly
            foreach (var permission in permissions)
            {
                if (!IsStorePolicy(permission) || string.IsNullOrEmpty(vm.StoreId))
                {
                    permissionsWithStoreIDs.Add(permission);
                }
                else
                {
                    permissionsWithStoreIDs.Add($"{permission}:{vm.StoreId}");
                }
            }

            var parsedPermissions = Permission.ToPermissions(permissionsWithStoreIDs.ToArray())
                .Where(permission => _permissionService.IsValidPolicy(permission.Policy))
                .GroupBy(permission => permission.Policy);

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

                    if (vm.SelectiveStores && permissionValue.IsStorePolicy &&
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

        private bool IsStorePolicy(string policy)
        => Permission.TryGetPolicyType(policy) is PolicyType.Store;

        private IActionResult HandleCommands(AddApiKeyViewModel viewModel)
        {
            if (string.IsNullOrEmpty(viewModel.Command))
            {
                return null;
            }
            var parts = viewModel.Command.Split(':', StringSplitOptions.RemoveEmptyEntries);
            var permission = parts[0];
            if (!IsStorePolicy(permission))
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
                UserId = User.GetId(),
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
                if (p.IsStorePolicy)
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
            var stores = await _StoreRepository.GetStoresByUserId(User.GetId());
            viewModel.Stores = stores.OrderBy(store => store.StoreName, StringComparer.InvariantCultureIgnoreCase).ToArray();

            var isAdmin = (await _authorizationService.AuthorizeAsync(User, Policies.CanModifyServerSettings))
                .Succeeded;
            viewModel.PermissionValues ??= _permissionService.Definitions.Values.OrderBy(d => d.Policy)
                .Select(definition => new AddApiKeyViewModel.PermissionValueItem()
                {
                    Permission = definition.Policy,
                    Value = false,
                    Forbidden = definition.Type is PolicyType.Server && !isAdmin
                }).ToList();

            foreach (var permissionValue in viewModel.PermissionValues)
            {
                if (permissionValue.Permission == null)
                    continue;
                if (!_permissionService.TryGetDefinition(permissionValue.Permission, out var definition))
                    continue;
                permissionValue.AllStoresTitle = definition.Display?.Title;
                permissionValue.AllStoresDescription = definition.Display?.Description;
                permissionValue.StoreSpecificTitle = definition.ScopeDisplay?.Title;
                permissionValue.StoreSpecificDescription = definition.ScopeDisplay?.Description;
            }

            if (!isAdmin)
            {
                foreach (var p in viewModel.PermissionValues.Where(item => item.Permission is null || Permission.TryGetPolicyType(item.Permission) is PolicyType.Server))
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
                public string Title
                {
                    get
                    {
                        if (StoreMode == ApiKeyStoreMode.Specific && !string.IsNullOrEmpty(StoreSpecificTitle))
                            return StoreSpecificTitle;
                        return AllStoresTitle;
                    }
                }
                public string Description
                {
                    get
                    {
                        if (StoreMode == ApiKeyStoreMode.Specific && !string.IsNullOrEmpty(StoreSpecificDescription))
                            return StoreSpecificDescription;
                        return AllStoresDescription;
                    }
                }
                public string AllStoresTitle { get; set; }
                public string AllStoresDescription { get; set; }
                public string StoreSpecificTitle { get; set; }
                public string StoreSpecificDescription { get; set; }

                private string _permission;
                public string Permission
                {
                    get => _permission;
                    set
                    {
                        BTCPayServer.Client.Permission.TryParse(value, out var permission);
                        _permission = permission?.ToString();
                        IsStorePolicy = permission?.Type == PolicyType.Store;
                    }
                }
                [BindNever]
                public bool IsStorePolicy { get; private set; }
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
