using BTCPayServer.Configuration;
using Microsoft.Extensions.Logging;
using BTCPayServer.HostedServices;
using BTCPayServer.Models;
using BTCPayServer.Models.ServerViewModels;
using BTCPayServer.Services;
using BTCPayServer.Services.Mails;
using BTCPayServer.Services.Rates;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;
using NBitcoin.DataEncoders;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Mail;
using System.Threading.Tasks;
using Renci.SshNet;
using BTCPayServer.Logging;
using BTCPayServer.Storage.Models;
using BTCPayServer.Storage.Services;
using BTCPayServer.Storage.Services.Providers;
using BTCPayServer.Services.Apps;
using Microsoft.AspNetCore.Mvc.Rendering;
using BTCPayServer.Data;

namespace BTCPayServer.Controllers
{
    [Authorize(Policy = Security.Policies.CanModifyServerSettings.Key,
               AuthenticationSchemes = Security.AuthenticationSchemes.Cookie)]
    public partial class ServerController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SettingsRepository _settingsRepository;
        private readonly NBXplorerDashboard _dashBoard;
        private readonly StoreRepository _storeRepository;
        private readonly LightningConfigurationProvider _lnConfigProvider;
        private readonly TorServices _torServices;
        private readonly BTCPayServerOptions _options;
        private readonly AppService _appService;
        private readonly CheckConfigurationHostedService _sshState;
        private readonly StoredFileRepository _storedFileRepository;
        private readonly FileService _fileService;
        private readonly IEnumerable<IStorageProviderService> _storageProviderServices;

        public ServerController(UserManager<ApplicationUser> userManager,
            StoredFileRepository storedFileRepository,
            FileService fileService,
            IEnumerable<IStorageProviderService> storageProviderServices,
            BTCPayServerOptions options,
            SettingsRepository settingsRepository,
            NBXplorerDashboard dashBoard,
            IHttpClientFactory httpClientFactory,
            LightningConfigurationProvider lnConfigProvider,
            TorServices torServices,
            StoreRepository storeRepository,
            AppService appService,
            CheckConfigurationHostedService sshState)
        {
            _options = options;
            _storedFileRepository = storedFileRepository;
            _fileService = fileService;
            _storageProviderServices = storageProviderServices;
            _userManager = userManager;
            _settingsRepository = settingsRepository;
            _dashBoard = dashBoard;
            HttpClientFactory = httpClientFactory;
            _storeRepository = storeRepository;
            _lnConfigProvider = lnConfigProvider;
            _torServices = torServices;
            _appService = appService;
            _sshState = sshState;
        }

        [Route("server/rates")]
        public async Task<IActionResult> Rates()
        {
            var rates = (await _settingsRepository.GetSettingAsync<RatesSetting>()) ?? new RatesSetting();

            var vm = new RatesViewModel()
            {
                CacheMinutes = rates.CacheInMinutes,
                PrivateKey = rates.PrivateKey,
                PublicKey = rates.PublicKey
            };
            await FetchRateLimits(vm);
            return View(vm);
        }

        private static async Task FetchRateLimits(RatesViewModel vm)
        {
            var coinAverage = GetCoinaverageService(vm, false);
            if (coinAverage != null)
            {
                try
                {
                    vm.RateLimits = await coinAverage.GetRateLimitsAsync();
                }
                catch { }
            }
        }

        [Route("server/rates")]
        [HttpPost]
        public async Task<IActionResult> Rates(RatesViewModel vm)
        {
            var rates = (await _settingsRepository.GetSettingAsync<RatesSetting>()) ?? new RatesSetting();
            rates.PrivateKey = vm.PrivateKey;
            rates.PublicKey = vm.PublicKey;
            rates.CacheInMinutes = vm.CacheMinutes;
            try
            {
                var service = GetCoinaverageService(vm, true);
                if (service != null)
                    await service.TestAuthAsync();
            }
            catch
            {
                ModelState.AddModelError(nameof(vm.PrivateKey), "Invalid API key pair");
            }
            if (!ModelState.IsValid)
            {
                await FetchRateLimits(vm);
                return View(vm);
            }
            await _settingsRepository.UpdateSetting(rates);
            TempData[WellKnownTempData.SuccessMessage] = "Rate settings successfully updated";
            return RedirectToAction(nameof(Rates));
        }

        private static CoinAverageRateProvider GetCoinaverageService(RatesViewModel vm, bool withAuth)
        {
            var settings = new CoinAverageSettings()
            {
                KeyPair = (vm.PublicKey, vm.PrivateKey)
            };
            if (!withAuth || settings.GetCoinAverageSignature() != null)
            {
                return new CoinAverageRateProvider()
                { Authenticator = settings };
            }
            return null;
        }

        [Route("server/users")]
        public IActionResult ListUsers(int skip = 0, int count = 50)
        {
            var users = new UsersViewModel();
            users.Users = _userManager.Users.Skip(skip).Take(count)
                .Select(u => new UsersViewModel.UserViewModel
                {
                    Name = u.UserName,
                    Email = u.Email,
                    Id = u.Id
                }).ToList();
            users.Skip = skip;
            users.Count = count;
            users.Total = _userManager.Users.Count();
            return View(users);
        }

        [Route("server/users/{userId}")]
        public new async Task<IActionResult> User(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
                return NotFound();
            var roles = await _userManager.GetRolesAsync(user);
            var userVm = new UserViewModel {Id = user.Id, Email = user.Email, IsAdmin = IsAdmin(roles)};
            return View(userVm);
        }

        [Route("server/maintenance")]
        public IActionResult Maintenance()
        {
            MaintenanceViewModel vm = new MaintenanceViewModel();
            vm.CanUseSSH = _sshState.CanUseSSH;
            if (!vm.CanUseSSH)
                TempData[WellKnownTempData.ErrorMessage] = "Maintenance feature requires access to SSH properly configured in BTCPayServer configuration";
            vm.DNSDomain = Request.Host.Host;
            if (IPAddress.TryParse(vm.DNSDomain, out var unused))
                vm.DNSDomain = null;
            return View(vm);
        }

        [Route("server/maintenance")]
        [HttpPost]
        public async Task<IActionResult> Maintenance(MaintenanceViewModel vm, string command)
        {
            vm.CanUseSSH = _sshState.CanUseSSH;
            if (!ModelState.IsValid)
                return View(vm);
            if (command == "changedomain")
            {
                if (string.IsNullOrWhiteSpace(vm.DNSDomain))
                {
                    ModelState.AddModelError(nameof(vm.DNSDomain), $"Required field");
                    return View(vm);
                }
                vm.DNSDomain = vm.DNSDomain.Trim().ToLowerInvariant();
                if (vm.DNSDomain.Equals(Request.Host.Host, StringComparison.OrdinalIgnoreCase))
                    return View(vm);
                if (IPAddress.TryParse(vm.DNSDomain, out var unused))
                {
                    ModelState.AddModelError(nameof(vm.DNSDomain), $"This should be a domain name");
                    return View(vm);
                }
                if (vm.DNSDomain.Equals(Request.Host.Host, StringComparison.InvariantCultureIgnoreCase))
                {
                    ModelState.AddModelError(nameof(vm.DNSDomain), $"The server is already set to use this domain");
                    return View(vm);
                }
                var builder = new UriBuilder();
                using (new HttpClient(new HttpClientHandler()
                {
                    ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
                }))
                {
                    try
                    {
                        builder.Scheme = Request.Scheme;
                        builder.Host = vm.DNSDomain;
                        var addresses1 = GetAddressAsync(Request.Host.Host);
                        var addresses2 = GetAddressAsync(vm.DNSDomain);
                        await Task.WhenAll(addresses1, addresses2);

                        var addressesSet = addresses1.GetAwaiter().GetResult().Select(c => c.ToString()).ToHashSet();
                        var hasCommonAddress = addresses2.GetAwaiter().GetResult().Select(c => c.ToString()).Any(s => addressesSet.Contains(s));
                        if (!hasCommonAddress)
                        {
                            ModelState.AddModelError(nameof(vm.DNSDomain), $"Invalid host ({vm.DNSDomain} is not pointing to this BTCPay instance)");
                            return View(vm);
                        }
                    }
                    catch (Exception ex)
                    {
                        var messages = new List<object>();
                        messages.Add(ex.Message);
                        if (ex.InnerException != null)
                            messages.Add(ex.InnerException.Message);
                        ModelState.AddModelError(nameof(vm.DNSDomain), $"Invalid domain ({string.Join(", ", messages.ToArray())})");
                        return View(vm);
                    }
                }

                var error = await RunSSH(vm, $"changedomain.sh {vm.DNSDomain}");
                if (error != null)
                    return error;

                builder.Path = null;
                builder.Query = null;
                TempData[WellKnownTempData.SuccessMessage] = $"Domain name changing... the server will restart, please use \"{builder.Uri.AbsoluteUri}\" (this page won't reload automatically)";
            }
            else if (command == "update")
            {
                var error = await RunSSH(vm, $"btcpay-update.sh");
                if (error != null)
                    return error;
                TempData[WellKnownTempData.SuccessMessage] = $"The server might restart soon if an update is available...  (this page won't reload automatically)";
            }
            else if (command == "clean")
            {
                var error = await RunSSH(vm, $"btcpay-clean.sh");
                if (error != null)
                    return error;
                TempData[WellKnownTempData.SuccessMessage] = $"The old docker images will be cleaned soon...";
            }
            else
            {
                return NotFound();
            }
            return RedirectToAction(nameof(Maintenance));
        }

        private Task<IPAddress[]> GetAddressAsync(string domainOrIP)
        {
            if (IPAddress.TryParse(domainOrIP, out var ip))
                return Task.FromResult(new[] { ip });
            return Dns.GetHostAddressesAsync(domainOrIP);
        }

        public static string RunId = Encoders.Hex.EncodeData(RandomUtils.GetBytes(32));
        [HttpGet]
        [Route("runid")]
        [AllowAnonymous]
        public IActionResult SeeRunId(string expected = null)
        {
            if (expected == RunId)
                return Ok();
            return BadRequest();
        }

        private async Task<IActionResult> RunSSH(MaintenanceViewModel vm, string command)
        {
            SshClient sshClient = null;

            try
            {
                sshClient = await _options.SSHSettings.ConnectAsync();
            }
            catch (Exception ex)
            {
                var message = ex.Message;
                if (ex is AggregateException aggrEx && aggrEx.InnerException?.Message != null)
                {
                    message = aggrEx.InnerException.Message;
                }
                ModelState.AddModelError(string.Empty, $"Connection problem ({message})");
                return View(vm);
            }
            _ = RunSSHCore(sshClient, $". /etc/profile.d/btcpay-env.sh && nohup {command} > /dev/null 2>&1 & disown");
            return null;
        }

        private static async Task RunSSHCore(SshClient sshClient, string ssh)
        {
            try
            {
                Logs.PayServer.LogInformation("Running SSH command: " + ssh);
                var result = await sshClient.RunBash(ssh, TimeSpan.FromMinutes(1.0));
                Logs.PayServer.LogInformation($"SSH command executed with exit status {result.ExitStatus}. Output: {result.Output}");
            }
            catch (Exception ex)
            {
                Logs.PayServer.LogWarning("Error while executing SSH command: " + ex.Message);
            }
            finally
            {
                sshClient.Dispose();
            }
        }

        private static bool IsAdmin(IList<string> roles)
        {
            return roles.Contains(Roles.ServerAdmin, StringComparer.Ordinal);
        }

        [Route("server/users/{userId}")]
        [HttpPost]
        public new async Task<IActionResult> User(string userId, UserViewModel viewModel)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
                return NotFound();

            var admins = await _userManager.GetUsersInRoleAsync(Roles.ServerAdmin);
            if (!viewModel.IsAdmin && admins.Count == 1)
            {
                TempData[WellKnownTempData.ErrorMessage] = "This is the only Admin, so their role can't be removed until another Admin is added.";
                return View(viewModel); // return
            }

            var roles = await _userManager.GetRolesAsync(user);
            if (viewModel.IsAdmin != IsAdmin(roles))
            {
                if (viewModel.IsAdmin)
                    await _userManager.AddToRoleAsync(user, Roles.ServerAdmin);
                else
                    await _userManager.RemoveFromRoleAsync(user, Roles.ServerAdmin);

                TempData[WellKnownTempData.SuccessMessage] = "User successfully updated";
            }

            return RedirectToAction(nameof(User), new { userId });
        }


        [Route("server/users/{userId}/delete")]
        public async Task<IActionResult> DeleteUser(string userId)
        {
            var user = userId == null ? null : await _userManager.FindByIdAsync(userId);
            if (user == null)
                return NotFound();

            var roles = await _userManager.GetRolesAsync(user);
            if (IsAdmin(roles))
            {
                var admins = await _userManager.GetUsersInRoleAsync(Roles.ServerAdmin);
                if (admins.Count == 1)
                {
                    // return
                    return View("Confirm", new ConfirmModel("Unable to Delete Last Admin",
                        "This is the last Admin, so it can't be removed"));
                }

                return View("Confirm", new ConfirmModel("Delete Admin " + user.Email,
                    "Are you sure you want to delete this Admin and delete all accounts, users and data associated with the server account?",
                    "Delete"));
            }
            else
            {
                return View("Confirm", new ConfirmModel("Delete user " + user.Email,
                                    "This user will be permanently deleted",
                                    "Delete"));
            }
        }

        [Route("server/users/{userId}/delete")]
        [HttpPost]
        public async Task<IActionResult> DeleteUserPost(string userId)
        {
            var user = userId == null ? null : await _userManager.FindByIdAsync(userId);
            if (user == null)
                return NotFound();
            await _userManager.DeleteAsync(user);
            await _storeRepository.CleanUnreachableStores();
            TempData[WellKnownTempData.SuccessMessage] = "User deleted";
            return RedirectToAction(nameof(ListUsers));
        }
        public IHttpClientFactory HttpClientFactory { get; }

        [Route("server/policies")]
        public async Task<IActionResult> Policies()
        {
            var data = (await _settingsRepository.GetSettingAsync<PoliciesSettings>()) ?? new PoliciesSettings();
            ViewBag.AppsList = await GetAppSelectList();
            return View(data);
        }

        [Route("server/policies")]
        [HttpPost]
        public async Task<IActionResult> Policies(PoliciesSettings settings, string command = "")
        {
            ViewBag.AppsList = await GetAppSelectList();
            if (command == "add-domain")
            {
                ModelState.Clear();
                settings.DomainToAppMapping.Add(new PoliciesSettings.DomainToAppMappingItem());
                return View(settings);
            }
            if (command.StartsWith("remove-domain", StringComparison.InvariantCultureIgnoreCase))
            {
                ModelState.Clear();
                var index = int.Parse(command.Substring(command.IndexOf(":", StringComparison.InvariantCultureIgnoreCase) + 1), CultureInfo.InvariantCulture);
                settings.DomainToAppMapping.RemoveAt(index);
                return View(settings);
            }

            if (!ModelState.IsValid)
            {
                return View(settings);
            }
            var appIdsToFetch = settings.DomainToAppMapping.Select(item => item.AppId).ToList();
            if (!string.IsNullOrEmpty(settings.RootAppId))
            {
                appIdsToFetch.Add(settings.RootAppId);
            }
            else
            {
                settings.RootAppType = null;
            }

            if (appIdsToFetch.Any())
            {
                var apps = (await _appService.GetApps(appIdsToFetch.ToArray()))
                    .ToDictionary(data => data.Id, data => Enum.Parse<AppType>(data.AppType));
                if (!string.IsNullOrEmpty(settings.RootAppId))
                {
                    settings.RootAppType = apps[settings.RootAppId];
                }

                foreach (var domainToAppMappingItem in settings.DomainToAppMapping)
                {
                    domainToAppMappingItem.AppType = apps[domainToAppMappingItem.AppId];
                }
            }

            await _settingsRepository.UpdateSetting(settings);
            TempData[WellKnownTempData.SuccessMessage] = "Policies updated successfully";
            return RedirectToAction(nameof(Policies));
        }

        [Route("server/services")]
        public async Task<IActionResult> Services()
        {
            var result = new ServicesViewModel();
            result.ExternalServices = _options.ExternalServices.ToList();

            // other services
            foreach (var externalService in _options.OtherExternalServices)
            {
                result.OtherExternalServices.Add(new ServicesViewModel.OtherExternalService()
                {
                    Name = externalService.Key,
                    Link = Request.GetAbsoluteUriNoPathBase(externalService.Value).AbsoluteUri
                });
            }
            if (CanShowSSHService())
            {
                result.OtherExternalServices.Add(new ServicesViewModel.OtherExternalService()
                {
                    Name = "SSH",
                    Link = Url.Action(nameof(SSHService))
                });
            }
            result.OtherExternalServices.Add(new ServicesViewModel.OtherExternalService()
            {
                Name = "Dynamic DNS",
                Link = Url.Action(nameof(DynamicDnsServices))
            });
            foreach (var torService in _torServices.Services)
            {
                if (torService.VirtualPort == 80)
                {
                    result.TorHttpServices.Add(new ServicesViewModel.OtherExternalService()
                    {
                        Name = torService.Name,
                        Link = $"http://{torService.OnionHost}"
                    });
                }
                else if (TryParseAsExternalService(torService, out var externalService))
                {
                    result.ExternalServices.Add(externalService);
                }
                else
                {
                    result.TorOtherServices.Add(new ServicesViewModel.OtherExternalService()
                    {
                        Name = torService.Name,
                        Link = $"{torService.OnionHost}:{torService.VirtualPort}"
                    });
                }
            }

            // external storage services
            var storageSettings = await _settingsRepository.GetSettingAsync<StorageSettings>();
            result.ExternalStorageServices.Add(new ServicesViewModel.OtherExternalService()
            {
                Name = storageSettings == null ? "Not set" : storageSettings.Provider.ToString(),
                Link = Url.Action("Storage")
            });
            return View(result);
        }

        private async Task<List<SelectListItem>> GetAppSelectList()
        {
            var apps = (await _appService.GetAllApps(null, true))
                .Select(a => new SelectListItem($"{a.AppType} - {a.AppName} - {a.StoreName}", a.Id)).ToList();
            apps.Insert(0, new SelectListItem("(None)", null));
            return apps;
        }

        private static bool TryParseAsExternalService(TorService torService, out ExternalService externalService)
        {
            externalService = null;
            if (torService.ServiceType == TorServiceType.P2P)
            {
                externalService = new ExternalService()
                {
                    CryptoCode = torService.Network.CryptoCode,
                    DisplayName = "Full node P2P",
                    Type = ExternalServiceTypes.P2P,
                    ConnectionString = new ExternalConnectionString(new Uri($"bitcoin-p2p://{torService.OnionHost}:{torService.VirtualPort}", UriKind.Absolute)),
                    ServiceName = torService.Name,
                };
            }
            if (torService.ServiceType == TorServiceType.RPC)
            {
                externalService = new ExternalService()
                {
                    CryptoCode = torService.Network.CryptoCode,
                    DisplayName = "Full node RPC",
                    Type = ExternalServiceTypes.RPC,
                    ConnectionString = new ExternalConnectionString(new Uri($"btcrpc://btcrpc:btcpayserver4ever@{torService.OnionHost}:{torService.VirtualPort}?label=BTCPayNode", UriKind.Absolute)),
                    ServiceName = torService.Name
                };
            }
            return externalService != null;
        }

        private ExternalService GetService(string serviceName, string cryptoCode)
        {
            var result = _options.ExternalServices.GetService(serviceName, cryptoCode);
            if (result != null)
                return result;
            foreach (var torService in _torServices.Services)
            {
                if (TryParseAsExternalService(torService, out var torExternalService) &&
                    torExternalService.ServiceName == serviceName)
                    return torExternalService;
            }
            return null;
        }

        [Route("server/services/{serviceName}/{cryptoCode}")]
        public async Task<IActionResult> Service(string serviceName, string cryptoCode, bool showQR = false, uint? nonce = null)
        {
            if (!_dashBoard.IsFullySynched(cryptoCode, out _))
            {
                TempData[WellKnownTempData.ErrorMessage] = $"{cryptoCode} is not fully synched";
                return RedirectToAction(nameof(Services));
            }
            var service = GetService(serviceName, cryptoCode);
            if (service == null)
                return NotFound();

            try
            {
                if (service.Type == ExternalServiceTypes.P2P)
                {
                    return View("P2PService", new LightningWalletServices()
                    {
                        ShowQR = showQR,
                        WalletName = service.ServiceName,
                        ServiceLink = service.ConnectionString.Server.AbsoluteUri.WithoutEndingSlash()
                    });
                }
                if (service.Type == ExternalServiceTypes.LNDSeedBackup)
                {
                    var model = LndSeedBackupViewModel.Parse(service.ConnectionString.CookieFilePath);
                    if (!model.IsWalletUnlockPresent)
                    {
                        TempData.SetStatusMessageModel(new StatusMessageModel()
                        {
                            Severity = StatusMessageModel.StatusSeverity.Warning,
                            Html = "Your LND does not seem to allow seed backup.<br />" +
                            "It's recommended, but not required, that you migrate as instructed by <a href=\"https://blog.btcpayserver.org/btcpay-lnd-migration\">our migration blog post</a>.<br />" +
                            "You will need to close all of your channels, and migrate your funds as <a href=\"https://blog.btcpayserver.org/btcpay-lnd-migration\">we documented</a>."
                        });
                    }
                    return View("LndSeedBackup", model);
                }
                if (service.Type == ExternalServiceTypes.RPC)
                {
                    return View("RPCService", new LightningWalletServices()
                    {
                        ShowQR = showQR,
                        WalletName = service.ServiceName,
                        ServiceLink = service.ConnectionString.Server.AbsoluteUri.WithoutEndingSlash()
                    });
                }
                var connectionString = await service.ConnectionString.Expand(Request.GetAbsoluteUriNoPathBase(), service.Type, _options.NetworkType);
                switch (service.Type)
                {
                    case ExternalServiceTypes.Charge:
                        return LightningChargeServices(connectionString);
                    case ExternalServiceTypes.RTL:
                    case ExternalServiceTypes.Spark:
                        if (connectionString.AccessKey == null)
                        {
                            TempData[WellKnownTempData.ErrorMessage] = $"The access key of the service is not set";
                            return RedirectToAction(nameof(Services));
                        }
                        LightningWalletServices vm = new LightningWalletServices();
                        vm.ShowQR = showQR;
                        vm.WalletName = service.DisplayName;
                        vm.ServiceLink = $"{connectionString.Server}?access-key={connectionString.AccessKey}";
                        return View("LightningWalletServices", vm);
                    case ExternalServiceTypes.LNDGRPC:
                    case ExternalServiceTypes.LNDRest:
                        return LndServices(service, connectionString, nonce);
                    default:
                        throw new NotSupportedException(service.Type.ToString());
                }
            }
            catch (Exception ex)
            {
                TempData[WellKnownTempData.ErrorMessage] = ex.Message;
                return RedirectToAction(nameof(Services));
            }
        }

        [HttpGet]
        [Route("server/services/{serviceName}/{cryptoCode}/removelndseed")]
        public IActionResult RemoveLndSeed(string serviceName, string cryptoCode)
        {
            return View("Confirm", new ConfirmModel()
            {
                Title = "Delete LND Seed",
                Description = "Please make sure you made a backup of the seed and password before deleting the LND backup seed from the server, are you sure to continue?",
                Action = "Delete"
            });
        }

        [HttpPost]
        [Route("server/services/{serviceName}/{cryptoCode}/removelndseed")]
        public async Task<IActionResult> RemoveLndSeedPost(string serviceName, string cryptoCode)
        {
            var service = GetService(serviceName, cryptoCode);
            if (service == null)
                return NotFound();

            var model = LndSeedBackupViewModel.Parse(service.ConnectionString.CookieFilePath);
            if (!model.IsWalletUnlockPresent)
            {
                TempData[WellKnownTempData.ErrorMessage] = $"File with wallet password and seed info not present";
                return RedirectToAction(nameof(Services));
            }

            if (string.IsNullOrEmpty(model.Seed))
            {
                TempData[WellKnownTempData.ErrorMessage] = $"Seed information was already removed";
                return RedirectToAction(nameof(Services));
            }

            if (await model.RemoveSeedAndWrite(service.ConnectionString.CookieFilePath))
            {
                TempData[WellKnownTempData.SuccessMessage] = $"Seed successfully removed";
                return RedirectToAction(nameof(Service), new { serviceName, cryptoCode });
            }
            else
            {
                TempData[WellKnownTempData.ErrorMessage] = $"Seed removal failed";
                return RedirectToAction(nameof(Services));
            }
        }

        private IActionResult LightningChargeServices(ExternalConnectionString connectionString)
        {
            ChargeServiceViewModel vm = new ChargeServiceViewModel
            {
                Uri = connectionString.Server.AbsoluteUri, APIToken = connectionString.APIToken
            };
            var builder = new UriBuilder(connectionString.Server) {UserName = "api-token", Password = vm.APIToken};
            vm.AuthenticatedUri = builder.ToString();
            return View(nameof(LightningChargeServices), vm);
        }

        private IActionResult LndServices(ExternalService service, ExternalConnectionString connectionString, uint? nonce)
        {
            var model = new LndServicesViewModel();
            switch (service.Type)
            {
                case ExternalServiceTypes.LNDGRPC:
                    model.Host = $"{connectionString.Server.DnsSafeHost}:{connectionString.Server.Port}";
                    model.SSL = connectionString.Server.Scheme == "https";
                    model.ConnectionType = "GRPC";
                    model.GRPCSSLCipherSuites = "ECDHE-RSA-AES128-GCM-SHA256:ECDHE-RSA-AES256-GCM-SHA384:ECDHE-ECDSA-AES128-SHA256";
                    break;
                case ExternalServiceTypes.LNDRest:
                    model.Uri = connectionString.Server.AbsoluteUri;
                    model.ConnectionType = "REST";
                    break;
            }

            if (connectionString.CertificateThumbprint != null)
            {
                model.CertificateThumbprint = connectionString.CertificateThumbprint;
            }
            if (connectionString.Macaroon != null)
            {
                model.Macaroon = Encoders.Hex.EncodeData(connectionString.Macaroon);
            }
            model.AdminMacaroon = connectionString.Macaroons?.AdminMacaroon?.Hex;
            model.InvoiceMacaroon = connectionString.Macaroons?.InvoiceMacaroon?.Hex;
            model.ReadonlyMacaroon = connectionString.Macaroons?.ReadonlyMacaroon?.Hex;

            if (nonce == null) return View(nameof(LndServices), model);
            
            var configKey = GetConfigKey("lnd", service.ServiceName, service.CryptoCode, nonce.Value);
            var lnConfig = _lnConfigProvider.GetConfig(configKey);
            
            if (lnConfig == null) return View(nameof(LndServices), model);
            
            model.QRCodeLink = Request.GetAbsoluteUri(Url.Action(nameof(GetLNDConfig), new { configKey }));
            model.QRCode = $"config={model.QRCodeLink}";

            return View(nameof(LndServices), model);
        }

        private static uint GetConfigKey(string type, string serviceName, string cryptoCode, uint nonce)
        {
            return (uint)HashCode.Combine(type, serviceName, cryptoCode, nonce);
        }

        [Route("lnd-config/{configKey}/lnd.config")]
        [AllowAnonymous]
        public IActionResult GetLNDConfig(uint configKey)
        {
            var conf = _lnConfigProvider.GetConfig(configKey);
            if (conf == null)
                return NotFound();
            return Json(conf);
        }

        [Route("server/services/{serviceName}/{cryptoCode}")]
        [HttpPost]
        public async Task<IActionResult> ServicePost(string serviceName, string cryptoCode)
        {
            if (!_dashBoard.IsFullySynched(cryptoCode, out NBXplorerDashboard.NBXplorerSummary _))
            {
                TempData[WellKnownTempData.ErrorMessage] = $"{cryptoCode} is not fully synched";
                return RedirectToAction(nameof(Services));
            }
            var service = GetService(serviceName, cryptoCode);
            if (service == null)
                return NotFound();

            ExternalConnectionString connectionString = null;
            try
            {
                connectionString = await service.ConnectionString.Expand(Request.GetAbsoluteUriNoPathBase(), service.Type, _options.NetworkType);
            }
            catch (Exception ex)
            {
                TempData[WellKnownTempData.ErrorMessage] = ex.Message;
                return RedirectToAction(nameof(Services));
            }

            LightningConfigurations confs = new LightningConfigurations();
            if (service.Type == ExternalServiceTypes.LNDGRPC)
            {
                LightningConfiguration grpcConf = new LightningConfiguration();
                grpcConf.Type = "grpc";
                grpcConf.Host = connectionString.Server.DnsSafeHost;
                grpcConf.Port = connectionString.Server.Port;
                grpcConf.SSL = connectionString.Server.Scheme == "https";
                confs.Configurations.Add(grpcConf);
            }
            else if (service.Type == ExternalServiceTypes.LNDRest)
            {
                var restconf = new LNDRestConfiguration();
                restconf.Type = "lnd-rest";
                restconf.Uri = connectionString.Server.AbsoluteUri;
                confs.Configurations.Add(restconf);
            }
            else
                throw new NotSupportedException(service.Type.ToString());
            var commonConf = (LNDConfiguration)confs.Configurations[confs.Configurations.Count - 1];
            commonConf.ChainType = _options.NetworkType.ToString();
            commonConf.CryptoCode = cryptoCode;
            commonConf.Macaroon = connectionString.Macaroon == null ? null : Encoders.Hex.EncodeData(connectionString.Macaroon);
            commonConf.CertificateThumbprint = connectionString.CertificateThumbprint == null ? null : connectionString.CertificateThumbprint;
            commonConf.AdminMacaroon = connectionString.Macaroons?.AdminMacaroon?.Hex;
            commonConf.ReadonlyMacaroon = connectionString.Macaroons?.ReadonlyMacaroon?.Hex;
            commonConf.InvoiceMacaroon = connectionString.Macaroons?.InvoiceMacaroon?.Hex;

            var nonce = RandomUtils.GetUInt32();
            var configKey = GetConfigKey("lnd", serviceName, cryptoCode, nonce);
            _lnConfigProvider.KeepConfig(configKey, confs);
            return RedirectToAction(nameof(Service), new { cryptoCode, serviceName, nonce });
        }


        [Route("server/services/dynamic-dns")]
        public async Task<IActionResult> DynamicDnsServices()
        {
            var settings = (await _settingsRepository.GetSettingAsync<DynamicDnsSettings>()) ?? new DynamicDnsSettings();
            return View(settings.Services.Select(s => new DynamicDnsViewModel()
            {
                Settings = s
            }).ToArray());
        }
        [Route("server/services/dynamic-dns/{hostname}")]
        public async Task<IActionResult> DynamicDnsServices(string hostname)
        {
            var settings = (await _settingsRepository.GetSettingAsync<DynamicDnsSettings>()) ?? new DynamicDnsSettings();
            var service = settings.Services.FirstOrDefault(s => s.Hostname.Equals(hostname, StringComparison.OrdinalIgnoreCase));
            if (service == null)
                return NotFound();
            var vm = new DynamicDnsViewModel();
            vm.Modify = true;
            vm.Settings = service;
            return View(nameof(DynamicDnsService), vm);
        }
        [Route("server/services/dynamic-dns")]
        [HttpPost]
        public async Task<IActionResult> DynamicDnsService(DynamicDnsViewModel viewModel, string command = null)
        {
            if (!ModelState.IsValid)
            {
                return View(viewModel);
            }
            if (command == "Save")
            {
                var settings = (await _settingsRepository.GetSettingAsync<DynamicDnsSettings>()) ?? new DynamicDnsSettings();
                var i = settings.Services.FindIndex(d => d.Hostname.Equals(viewModel.Settings.Hostname, StringComparison.OrdinalIgnoreCase));
                if (i != -1)
                {
                    ModelState.AddModelError(nameof(viewModel.Settings.Hostname), "This hostname already exists");
                    return View(viewModel);
                }
                if (viewModel.Settings.Hostname != null)
                    viewModel.Settings.Hostname = viewModel.Settings.Hostname.Trim().ToLowerInvariant();
                string errorMessage = await viewModel.Settings.SendUpdateRequest(HttpClientFactory.CreateClient());
                if (errorMessage == null)
                {
                    TempData[WellKnownTempData.SuccessMessage] = $"The Dynamic DNS has been successfully queried, your configuration is saved";
                    viewModel.Settings.LastUpdated = DateTimeOffset.UtcNow;
                    settings.Services.Add(viewModel.Settings);
                    await _settingsRepository.UpdateSetting(settings);
                    return RedirectToAction(nameof(DynamicDnsServices));
                }
                else
                {
                    ModelState.AddModelError(string.Empty, errorMessage);
                    return View(viewModel);
                }
            }
            else
            {
                return View(new DynamicDnsViewModel() { Settings = new DynamicDnsService() });
            }
        }
        
        [Route("server/services/dynamic-dns/{hostname}")]
        [HttpPost]
        public async Task<IActionResult> DynamicDnsService(DynamicDnsViewModel viewModel, string hostname, string command = null)
        {
            if (!ModelState.IsValid)
            {
                return View(viewModel);
            }
            var settings = (await _settingsRepository.GetSettingAsync<DynamicDnsSettings>()) ?? new DynamicDnsSettings();

            var i = settings.Services.FindIndex(d => d.Hostname.Equals(hostname, StringComparison.OrdinalIgnoreCase));
            if (i == -1)
                return NotFound();
            if (viewModel.Settings.Password == null)
                viewModel.Settings.Password = settings.Services[i].Password;
            if (viewModel.Settings.Hostname != null)
                viewModel.Settings.Hostname = viewModel.Settings.Hostname.Trim().ToLowerInvariant();
            if (!viewModel.Settings.Enabled)
            {
                TempData[WellKnownTempData.SuccessMessage] = $"The Dynamic DNS service has been disabled";
                viewModel.Settings.LastUpdated = null;
            }
            else
            {
                string errorMessage = await viewModel.Settings.SendUpdateRequest(HttpClientFactory.CreateClient());
                if (errorMessage == null)
                {
                    TempData[WellKnownTempData.SuccessMessage] = $"The Dynamic DNS has been successfully queried, your configuration is saved";
                    viewModel.Settings.LastUpdated = DateTimeOffset.UtcNow;
                }
                else
                {
                    ModelState.AddModelError(string.Empty, errorMessage);
                    return View(viewModel);
                }
            }
            settings.Services[i] = viewModel.Settings;
            await _settingsRepository.UpdateSetting(settings);
            RouteData.Values.Remove(nameof(hostname));
            return RedirectToAction(nameof(DynamicDnsServices));
        }
        
        [HttpGet]
        [Route("server/services/dynamic-dns/{hostname}/delete")]
        public async Task<IActionResult> DeleteDynamicDnsService(string hostname)
        {
            var settings = (await _settingsRepository.GetSettingAsync<DynamicDnsSettings>()) ?? new DynamicDnsSettings();
            var i = settings.Services.FindIndex(d => d.Hostname.Equals(hostname, StringComparison.OrdinalIgnoreCase));
            if (i == -1)
                return NotFound();
            return View("Confirm", new ConfirmModel()
            {
                Title = "Delete the dynamic dns service for " + hostname,
                Description = "BTCPayServer will stop updating this DNS record periodically",
                Action = "Delete"
            });
        }
        [HttpPost]
        [Route("server/services/dynamic-dns/{hostname}/delete")]
        public async Task<IActionResult> DeleteDynamicDnsServicePost(string hostname)
        {
            var settings = (await _settingsRepository.GetSettingAsync<DynamicDnsSettings>()) ?? new DynamicDnsSettings();
            var i = settings.Services.FindIndex(d => d.Hostname.Equals(hostname, StringComparison.OrdinalIgnoreCase));
            if (i == -1)
                return NotFound();
            settings.Services.RemoveAt(i);
            await _settingsRepository.UpdateSetting(settings);
            TempData[WellKnownTempData.SuccessMessage] = "Dynamic DNS service successfully removed";
            RouteData.Values.Remove(nameof(hostname));
            return RedirectToAction(nameof(DynamicDnsServices));
        }

        [Route("server/services/ssh")]
        public async Task<IActionResult> SSHService()
        {
            if (!CanShowSSHService())
                return NotFound();

            var settings = _options.SSHSettings;
            var server = Extensions.IsLocalNetwork(settings.Server) ? Request.Host.Host : settings.Server;
            SSHServiceViewModel vm = new SSHServiceViewModel();
            string port = settings.Port == 22 ? "" : $" -p {settings.Port}";
            vm.CommandLine = $"ssh {settings.Username}@{server}{port}";
            vm.Password = settings.Password;
            vm.KeyFilePassword = settings.KeyFilePassword;
            vm.HasKeyFile = !string.IsNullOrEmpty(settings.KeyFile);

            //  Let's try to just read the authorized key file
            if (CanAccessAuthorizedKeyFile())
            {
                try
                {
                    vm.SSHKeyFileContent = await System.IO.File.ReadAllTextAsync(settings.AuthorizedKeysFile);
                }
                catch { }
            }

            // If that fail, just fallback to ssh
            if (vm.SSHKeyFileContent == null && _sshState.CanUseSSH)
            {
                try
                {
                    using (var sshClient = await _options.SSHSettings.ConnectAsync())
                    {
                        var result = await sshClient.RunBash("cat ~/.ssh/authorized_keys", TimeSpan.FromSeconds(10));
                        vm.SSHKeyFileContent = result.Output;
                    }
                }
                catch { }
            }
            return View(vm);
        }

        bool CanShowSSHService()
        {
            return _options.SSHSettings != null && (_sshState.CanUseSSH || CanAccessAuthorizedKeyFile());
        }

        private bool CanAccessAuthorizedKeyFile()
        {
            return _options.SSHSettings?.AuthorizedKeysFile != null && System.IO.File.Exists(_options.SSHSettings.AuthorizedKeysFile);
        }

        [HttpPost]
        [Route("server/services/ssh")]
        public async Task<IActionResult> SSHService(SSHServiceViewModel viewModel)
        {
            string newContent = viewModel?.SSHKeyFileContent ?? string.Empty;
            newContent = newContent.Replace("\r\n", "\n", StringComparison.OrdinalIgnoreCase);

            bool updated = false;
            Exception exception = null;
            // Let's try to just write the file
            if (CanAccessAuthorizedKeyFile())
            {
                try
                {
                    await System.IO.File.WriteAllTextAsync(_options.SSHSettings.AuthorizedKeysFile, newContent);
                    TempData[WellKnownTempData.SuccessMessage] = "authorized_keys has been updated";
                    updated = true;
                }
                catch (Exception ex)
                {
                    exception = ex;
                }
            }

            // If that fail, fallback to ssh
            if (!updated && _sshState.CanUseSSH)
            {
                try
                {
                    using (var sshClient = await _options.SSHSettings.ConnectAsync())
                    {
                        await sshClient.RunBash($"mkdir -p ~/.ssh && echo '{newContent.EscapeSingleQuotes()}' > ~/.ssh/authorized_keys", TimeSpan.FromSeconds(10));
                    }
                    updated = true;
                    exception = null;
                }
                catch (Exception ex)
                {
                    exception = ex;
                }
            }

            if (exception is null)
            {
                TempData[WellKnownTempData.SuccessMessage] = "authorized_keys has been updated";
            }
            else
            {
                TempData[WellKnownTempData.ErrorMessage] = exception.Message;
            }
            return RedirectToAction(nameof(SSHService));
        }

        [Route("server/theme")]
        public async Task<IActionResult> Theme()
        {
            var data = (await _settingsRepository.GetSettingAsync<ThemeSettings>()) ?? new ThemeSettings();
            return View(data);
        }
        
        [Route("server/theme")]
        [HttpPost]
        public async Task<IActionResult> Theme(ThemeSettings settings)
        {
            await _settingsRepository.UpdateSetting(settings);
            TempData[WellKnownTempData.SuccessMessage] = "Theme settings updated successfully";
            return View(settings);
        }

        [Route("server/emails")]
        public async Task<IActionResult> Emails()
        {
            var data = (await _settingsRepository.GetSettingAsync<EmailSettings>()) ?? new EmailSettings();
            return View(new EmailsViewModel() { Settings = data });
        }

        [Route("server/emails")]
        [HttpPost]
        public async Task<IActionResult> Emails(EmailsViewModel model, string command)
        {
            if (!model.Settings.IsComplete())
            {
                TempData[WellKnownTempData.ErrorMessage] = "Required fields missing";
                return View(model);
            }

            if (command == "Test")
            {
                try
                {
                    var client = model.Settings.CreateSmtpClient();
                    var message = model.Settings.CreateMailMessage(new MailAddress(model.TestEmail), "BTCPay test", "BTCPay test");
                    await client.SendMailAsync(message);
                    TempData[WellKnownTempData.SuccessMessage] = "Email sent to " + model.TestEmail + ", please, verify you received it";
                }
                catch (Exception ex)
                {
                    TempData[WellKnownTempData.ErrorMessage] = ex.Message;
                }
                return View(model);
            }
            else // if(command == "Save")
            {
                await _settingsRepository.UpdateSetting(model.Settings);
                TempData[WellKnownTempData.SuccessMessage] = "Email settings saved";
                return View(model);
            }
        }

        [Route("server/logs/{file?}")]
        public async Task<IActionResult> LogsView(string file = null, int offset = 0)
        {
            if (offset < 0)
            {
                offset = 0;
            }

            var vm = new LogsViewModel();

            if (string.IsNullOrEmpty(_options.LogFile))
            {
                TempData[WellKnownTempData.ErrorMessage] = "File Logging Option not specified. " +
                                   "You need to set debuglog and optionally " +
                                   "debugloglevel in the configuration or through runtime arguments";
            }
            else
            {
                var di = Directory.GetParent(_options.LogFile);
                if (di == null)
                {
                    TempData[WellKnownTempData.ErrorMessage] = "Could not load log files";
                    
                    return View("Logs", vm);
                }

                var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(_options.LogFile);
                var fileExtension = Path.GetExtension(_options.LogFile) ?? string.Empty;
                var logFiles = di.GetFiles($"{fileNameWithoutExtension}*{fileExtension}");
                vm.LogFileCount = logFiles.Length;
                vm.LogFiles = logFiles
                    .OrderBy(info => info.LastWriteTime)
                    .Skip(offset)
                    .Take(5)
                    .ToList();
                vm.LogFileOffset = offset;

                if (string.IsNullOrEmpty(file) || !file.EndsWith(fileExtension, StringComparison.Ordinal))
                    return View("Logs", vm);
                vm.Log = "";
                var fi = vm.LogFiles.FirstOrDefault(o => o.Name == file);
                if (fi == null)
                    return NotFound();
                try
                {
                    using (var fileStream = new FileStream(
                        fi.FullName,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite))
                    {
                        using (var reader = new StreamReader(fileStream))
                        {
                            vm.Log = await reader.ReadToEndAsync();
                        }
                    }
                }
                catch
                {
                    return NotFound();
                }
            }

            return View("Logs", vm);
        }
    }
}
