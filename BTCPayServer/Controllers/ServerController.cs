using BTCPayServer.Configuration;
using Microsoft.Extensions.Logging;
using BTCPayServer.HostedServices;
using BTCPayServer.Models;
using BTCPayServer.Models.ServerViewModels;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.Services;
using BTCPayServer.Services.Mails;
using BTCPayServer.Services.Rates;
using BTCPayServer.Services.Stores;
using BTCPayServer.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;
using NBitcoin.DataEncoders;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Mail;
using System.Threading.Tasks;
using Renci.SshNet;
using BTCPayServer.Logging;
using BTCPayServer.Lightning;
using System.Runtime.CompilerServices;
using BTCPayServer.Storage.Models;
using BTCPayServer.Storage.Services;
using BTCPayServer.Storage.Services.Providers;
using BTCPayServer.Services.Apps;
using Microsoft.AspNetCore.Mvc.Rendering;
using BTCPayServer.Data;

namespace BTCPayServer.Controllers
{
    [Authorize(Policy = BTCPayServer.Security.Policies.CanModifyServerSettings.Key)]
    public partial class ServerController : Controller
    {
        private UserManager<ApplicationUser> _UserManager;
        SettingsRepository _SettingsRepository;
        private readonly NBXplorerDashboard _dashBoard;
        private RateFetcher _RateProviderFactory;
        private StoreRepository _StoreRepository;
        LightningConfigurationProvider _LnConfigProvider;
        private readonly TorServices _torServices;
        BTCPayServerOptions _Options;
        ApplicationDbContextFactory _ContextFactory;
        private readonly StoredFileRepository _StoredFileRepository;
        private readonly FileService _FileService;
        private readonly IEnumerable<IStorageProviderService> _StorageProviderServices;

        public ServerController(UserManager<ApplicationUser> userManager,
            StoredFileRepository storedFileRepository,
            FileService fileService,
            IEnumerable<IStorageProviderService> storageProviderServices,
            BTCPayServerOptions options,
            RateFetcher rateProviderFactory,
            SettingsRepository settingsRepository,
            NBXplorerDashboard dashBoard,
            IHttpClientFactory httpClientFactory,
            LightningConfigurationProvider lnConfigProvider,
            TorServices torServices,
            StoreRepository storeRepository,
            ApplicationDbContextFactory contextFactory)
        {
            _Options = options;
            _StoredFileRepository = storedFileRepository;
            _FileService = fileService;
            _StorageProviderServices = storageProviderServices;
            _UserManager = userManager;
            _SettingsRepository = settingsRepository;
            _dashBoard = dashBoard;
            HttpClientFactory = httpClientFactory;
            _RateProviderFactory = rateProviderFactory;
            _StoreRepository = storeRepository;
            _LnConfigProvider = lnConfigProvider;
            _torServices = torServices;
            _ContextFactory = contextFactory;
        }

        [Route("server/rates")]
        public async Task<IActionResult> Rates()
        {
            var rates = (await _SettingsRepository.GetSettingAsync<RatesSetting>()) ?? new RatesSetting();

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
            var rates = (await _SettingsRepository.GetSettingAsync<RatesSetting>()) ?? new RatesSetting();
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
            await _SettingsRepository.UpdateSetting(rates);
            StatusMessage = "Rate settings successfully updated";
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
        public IActionResult ListUsers()
        {
            var users = new UsersViewModel();
            users.StatusMessage = StatusMessage;
            users.Users
                = _UserManager.Users.Select(u => new UsersViewModel.UserViewModel()
                {
                    Name = u.UserName,
                    Email = u.Email,
                    Id = u.Id
                }).ToList();
            return View(users);
        }

        [Route("server/users/{userId}")]
        public new async Task<IActionResult> User(string userId)
        {
            var user = await _UserManager.FindByIdAsync(userId);
            if (user == null)
                return NotFound();
            var roles = await _UserManager.GetRolesAsync(user);
            var userVM = new UserViewModel();
            userVM.Id = user.Id;
            userVM.Email = user.Email;
            userVM.IsAdmin = IsAdmin(roles);
            return View(userVM);
        }

        [Route("server/maintenance")]
        public IActionResult Maintenance()
        {
            MaintenanceViewModel vm = new MaintenanceViewModel();
            vm.UserName = "btcpayserver";
            vm.DNSDomain = this.Request.Host.Host;
            vm.SetConfiguredSSH(_Options.SSHSettings);
            if (IPAddress.TryParse(vm.DNSDomain, out var unused))
                vm.DNSDomain = null;
            return View(vm);
        }

        [Route("server/maintenance")]
        [HttpPost]
        public async Task<IActionResult> Maintenance(MaintenanceViewModel vm, string command)
        {
            if (!ModelState.IsValid)
                return View(vm);
            vm.SetConfiguredSSH(_Options.SSHSettings);
            if (command == "changedomain")
            {
                if (string.IsNullOrWhiteSpace(vm.DNSDomain))
                {
                    ModelState.AddModelError(nameof(vm.DNSDomain), $"Required field");
                    return View(vm);
                }
                vm.DNSDomain = vm.DNSDomain.Trim().ToLowerInvariant();
                if (vm.DNSDomain.Equals(this.Request.Host.Host, StringComparison.OrdinalIgnoreCase))
                    return View(vm);
                if (IPAddress.TryParse(vm.DNSDomain, out var unused))
                {
                    ModelState.AddModelError(nameof(vm.DNSDomain), $"This should be a domain name");
                    return View(vm);
                }
                if (vm.DNSDomain.Equals(this.Request.Host.Host, StringComparison.InvariantCultureIgnoreCase))
                {
                    ModelState.AddModelError(nameof(vm.DNSDomain), $"The server is already set to use this domain");
                    return View(vm);
                }
                var builder = new UriBuilder();
                using (var client = new HttpClient(new HttpClientHandler()
                {
                    ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
                }))
                {
                    try
                    {
                        builder.Scheme = this.Request.Scheme;
                        builder.Host = vm.DNSDomain;
                        var addresses1 = GetAddressAsync(this.Request.Host.Host);
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

                var error = RunSSH(vm, $"changedomain.sh {vm.DNSDomain}");
                if (error != null)
                    return error;

                builder.Path = null;
                builder.Query = null;
                StatusMessage = $"Domain name changing... the server will restart, please use \"{builder.Uri.AbsoluteUri}\"";
            }
            else if (command == "update")
            {
                var error = RunSSH(vm, $"btcpay-update.sh");
                if (error != null)
                    return error;
                StatusMessage = $"The server might restart soon if an update is available...";
            }
            else if (command == "clean")
            {
                var error = RunSSH(vm, $"btcpay-clean.sh");
                if (error != null)
                    return error;
                StatusMessage = $"The old docker images will be cleaned soon...";
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

        public static string RunId = Encoders.Hex.EncodeData(NBitcoin.RandomUtils.GetBytes(32));
        [HttpGet]
        [Route("runid")]
        [AllowAnonymous]
        public IActionResult SeeRunId(string expected = null)
        {
            if (expected == RunId)
                return Ok();
            return BadRequest();
        }

        private IActionResult RunSSH(MaintenanceViewModel vm, string ssh)
        {
            ssh = $"sudo bash -c '. /etc/profile.d/btcpay-env.sh && nohup {ssh} > /dev/null 2>&1 & disown'";
            var sshClient = _Options.SSHSettings == null ? vm.CreateSSHClient(this.Request.Host.Host)
                                                         : new SshClient(_Options.SSHSettings.CreateConnectionInfo());

            if (_Options.TrustedFingerprints.Count != 0)
            {
                sshClient.HostKeyReceived += (object sender, Renci.SshNet.Common.HostKeyEventArgs e) =>
                {
                    if (_Options.TrustedFingerprints.Count == 0)
                    {
                        Logs.Configuration.LogWarning($"SSH host fingerprint for {e.HostKeyName} is untrusted, start BTCPay with -sshtrustedfingerprints \"{Encoders.Hex.EncodeData(e.FingerPrint)}\"");
                        e.CanTrust = true; // Not a typo, we want the connection to succeed with a warning
                    }
                    else
                    {
                        e.CanTrust = _Options.IsTrustedFingerprint(e.FingerPrint, e.HostKey);
                        if (!e.CanTrust)
                            Logs.Configuration.LogError($"SSH host fingerprint for {e.HostKeyName} is untrusted, start BTCPay with -sshtrustedfingerprints \"{Encoders.Hex.EncodeData(e.FingerPrint)}\"");
                    }
                };
            }
            else
            {

            }

            try
            {
                sshClient.Connect();
            }
            catch (Renci.SshNet.Common.SshAuthenticationException)
            {
                ModelState.AddModelError(nameof(vm.Password), "Invalid credentials");
                sshClient.Dispose();
                return View(vm);
            }
            catch (Exception ex)
            {
                var message = ex.Message;
                if (ex is AggregateException aggrEx && aggrEx.InnerException?.Message != null)
                {
                    message = aggrEx.InnerException.Message;
                }
                ModelState.AddModelError(nameof(vm.UserName), $"Connection problem ({message})");
                sshClient.Dispose();
                return View(vm);
            }

            var sshCommand = sshClient.CreateCommand(ssh);
            sshCommand.CommandTimeout = TimeSpan.FromMinutes(1.0);
            sshCommand.BeginExecute(ar =>
            {
                try
                {
                    Logs.PayServer.LogInformation("Running SSH command: " + ssh);
                    var result = sshCommand.EndExecute(ar);
                    Logs.PayServer.LogInformation("SSH command executed: " + result);
                }
                catch (Exception ex)
                {
                    Logs.PayServer.LogWarning("Error while executing SSH command: " + ex.Message);
                }
                sshClient.Dispose();
            });
            return null;
        }

        private static bool IsAdmin(IList<string> roles)
        {
            return roles.Contains(Roles.ServerAdmin, StringComparer.Ordinal);
        }

        [Route("server/users/{userId}")]
        [HttpPost]
        public new async Task<IActionResult> User(string userId, UserViewModel viewModel)
        {
            var user = await _UserManager.FindByIdAsync(userId);
            if (user == null)
                return NotFound();

            viewModel.StatusMessage = "";

            var admins = await _UserManager.GetUsersInRoleAsync(Roles.ServerAdmin);
            if (!viewModel.IsAdmin && admins.Count == 1)
            {
                viewModel.StatusMessage = "This is the only Admin, so their role can't be removed until another Admin is added.";
                return View(viewModel); // return
            }

            var roles = await _UserManager.GetRolesAsync(user);
            if (viewModel.IsAdmin != IsAdmin(roles))
            {
                if (viewModel.IsAdmin)
                    await _UserManager.AddToRoleAsync(user, Roles.ServerAdmin);
                else
                    await _UserManager.RemoveFromRoleAsync(user, Roles.ServerAdmin);

                viewModel.StatusMessage = "User successfully updated";
            }

            return View(viewModel);
        }


        [Route("server/users/{userId}/delete")]
        public async Task<IActionResult> DeleteUser(string userId)
        {
            var user = userId == null ? null : await _UserManager.FindByIdAsync(userId);
            if (user == null)
                return NotFound();

            var roles = await _UserManager.GetRolesAsync(user);
            if (IsAdmin(roles))
            {
                var admins = await _UserManager.GetUsersInRoleAsync(Roles.ServerAdmin);
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
            var user = userId == null ? null : await _UserManager.FindByIdAsync(userId);
            if (user == null)
                return NotFound();
            await _UserManager.DeleteAsync(user);
            await _StoreRepository.CleanUnreachableStores();
            StatusMessage = "User deleted";
            return RedirectToAction(nameof(ListUsers));
        }

        [TempData]
        public string StatusMessage
        {
            get; set;
        }
        public IHttpClientFactory HttpClientFactory { get; }

        [Route("server/policies")]
        public async Task<IActionResult> Policies()
        {
            var data = (await _SettingsRepository.GetSettingAsync<PoliciesSettings>()) ?? new PoliciesSettings();

            // load display app dropdown
            using (var ctx = _ContextFactory.CreateContext())
            {
                var userId = _UserManager.GetUserId(base.User);
                var selectList = ctx.Users.Where(user => user.Id == userId)
                                .SelectMany(s => s.UserStores)
                                .Select(s => s.StoreData)
                                .SelectMany(s => s.Apps)
                                .Select(a => new SelectListItem($"{a.AppType} - {a.Name}", a.Id)).ToList();
                selectList.Insert(0, new SelectListItem("(None)", null));
                ViewBag.AppsList = new SelectList(selectList, "Value", "Text", data.RootAppId);
            }

            return View(data);
        }
        [Route("server/policies")]
        [HttpPost]
        public async Task<IActionResult> Policies(PoliciesSettings settings)
        {
            if (!String.IsNullOrEmpty(settings.RootAppId))
            {
                using (var ctx = _ContextFactory.CreateContext())
                {
                    var app = ctx.Apps.SingleOrDefault(a => a.Id == settings.RootAppId);
                    if (app != null)
                        settings.RootAppType = Enum.Parse<AppType>(app.AppType);
                    else
                        settings.RootAppType = null;
                }
            }
            else
            {
                // not preserved on client side, but clearing it just in case
                settings.RootAppType = null;
            }

            await _SettingsRepository.UpdateSetting(settings);
            TempData["StatusMessage"] = "Policies updated successfully";
            return RedirectToAction(nameof(Policies));
        }

        [Route("server/services")]
        public async Task<IActionResult> Services()
        {
            var result = new ServicesViewModel();
            result.ExternalServices = _Options.ExternalServices.ToList();
            foreach (var externalService in _Options.OtherExternalServices)
            {
                result.OtherExternalServices.Add(new ServicesViewModel.OtherExternalService()
                {
                    Name = externalService.Key,
                    Link = this.Request.GetAbsoluteUriNoPathBase(externalService.Value).AbsoluteUri
                });
            }
            if (_Options.SSHSettings != null)
            {
                result.OtherExternalServices.Add(new ServicesViewModel.OtherExternalService()
                {
                    Name = "SSH",
                    Link = this.Url.Action(nameof(SSHService))
                });
            }
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

            var storageSettings = await _SettingsRepository.GetSettingAsync<StorageSettings>();
            result.ExternalStorageServices.Add(new ServicesViewModel.OtherExternalService()
            {
                Name = storageSettings == null? "Not set": storageSettings.Provider.ToString(),
                Link = Url.Action("Storage")
            });
            return View(result);
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
            return externalService != null;
        }

        private ExternalService GetService(string serviceName, string cryptoCode)
        {
            var result = _Options.ExternalServices.GetService(serviceName, cryptoCode);
            if (result != null)
                return result;
            _torServices.Services.FirstOrDefault(s => TryParseAsExternalService(s, out result));
            return result;
        }

        [Route("server/services/{serviceName}/{cryptoCode}")]
        public async Task<IActionResult> Service(string serviceName, string cryptoCode, bool showQR = false, uint? nonce = null)
        {
            if (!_dashBoard.IsFullySynched(cryptoCode, out var unusud))
            {
                StatusMessage = $"Error: {cryptoCode} is not fully synched";
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
                var connectionString = await service.ConnectionString.Expand(this.Request.GetAbsoluteUriNoPathBase(), service.Type);
                switch (service.Type)
                {
                    case ExternalServiceTypes.Charge:
                        return LightningChargeServices(service, connectionString, showQR);
                    case ExternalServiceTypes.RTL:
                    case ExternalServiceTypes.Spark:
                        if (connectionString.AccessKey == null)
                        {
                            StatusMessage = $"Error: The access key of the service is not set";
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
                StatusMessage = $"Error: {ex.Message}";
                return RedirectToAction(nameof(Services));
            }
        }

        private IActionResult LightningChargeServices(ExternalService service, ExternalConnectionString connectionString, bool showQR = false)
        {
            ChargeServiceViewModel vm = new ChargeServiceViewModel();
            vm.Uri = connectionString.Server.AbsoluteUri;
            vm.APIToken = connectionString.APIToken;
            var builder = new UriBuilder(connectionString.Server);
            builder.UserName = "api-token";
            builder.Password = vm.APIToken;
            vm.AuthenticatedUri = builder.ToString();
            return View(nameof(LightningChargeServices), vm);
        }

        private IActionResult LndServices(ExternalService service, ExternalConnectionString connectionString, uint? nonce)
        {
            var model = new LndGrpcServicesViewModel();
            if (service.Type == ExternalServiceTypes.LNDGRPC)
            {
                model.Host = $"{connectionString.Server.DnsSafeHost}:{connectionString.Server.Port}";
                model.SSL = connectionString.Server.Scheme == "https";
                model.ConnectionType = "GRPC";
                model.GRPCSSLCipherSuites = "ECDHE-RSA-AES128-GCM-SHA256:ECDHE-RSA-AES256-GCM-SHA384:ECDHE-ECDSA-AES128-SHA256";
            }
            else if (service.Type == ExternalServiceTypes.LNDRest)
            {
                model.Uri = connectionString.Server.AbsoluteUri;
                model.ConnectionType = "REST";
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

            if (nonce != null)
            {
                var configKey = GetConfigKey("lnd", service.ServiceName, service.CryptoCode, nonce.Value);
                var lnConfig = _LnConfigProvider.GetConfig(configKey);
                if (lnConfig != null)
                {
                    model.QRCodeLink = Request.GetAbsoluteUri(Url.Action(nameof(GetLNDConfig), new { configKey = configKey }));
                    model.QRCode = $"config={model.QRCodeLink}";
                }
            }

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
            var conf = _LnConfigProvider.GetConfig(configKey);
            if (conf == null)
                return NotFound();
            return Json(conf);
        }

        [Route("server/services/{serviceName}/{cryptoCode}")]
        [HttpPost]
        public async Task<IActionResult> ServicePost(string serviceName, string cryptoCode)
        {
            if (!_dashBoard.IsFullySynched(cryptoCode, out var unusud))
            {
                StatusMessage = $"Error: {cryptoCode} is not fully synched";
                return RedirectToAction(nameof(Services));
            }
            var service = GetService(serviceName, cryptoCode);
            if (service == null)
                return NotFound();

            ExternalConnectionString connectionString = null;
            try
            {
                connectionString = await service.ConnectionString.Expand(this.Request.GetAbsoluteUriNoPathBase(), service.Type);
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
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
            commonConf.ChainType = _Options.NetworkType.ToString();
            commonConf.CryptoCode = cryptoCode;
            commonConf.Macaroon = connectionString.Macaroon == null ? null : Encoders.Hex.EncodeData(connectionString.Macaroon);
            commonConf.CertificateThumbprint = connectionString.CertificateThumbprint == null ? null : connectionString.CertificateThumbprint;
            commonConf.AdminMacaroon = connectionString.Macaroons?.AdminMacaroon?.Hex;
            commonConf.ReadonlyMacaroon = connectionString.Macaroons?.ReadonlyMacaroon?.Hex;
            commonConf.InvoiceMacaroon = connectionString.Macaroons?.InvoiceMacaroon?.Hex;

            var nonce = RandomUtils.GetUInt32();
            var configKey = GetConfigKey("lnd", serviceName, cryptoCode, nonce);
            _LnConfigProvider.KeepConfig(configKey, confs);
            return RedirectToAction(nameof(Service), new { cryptoCode = cryptoCode, serviceName = serviceName, nonce = nonce });
        }

        [Route("server/services/ssh")]
        public IActionResult SSHService(bool downloadKeyFile = false)
        {
            var settings = _Options.SSHSettings;
            if (settings == null)
                return NotFound();
            if (downloadKeyFile)
            {
                if (!System.IO.File.Exists(settings.KeyFile))
                    return NotFound();
                return File(System.IO.File.ReadAllBytes(settings.KeyFile), "application/octet-stream", "id_rsa");
            }

            var server = Extensions.IsLocalNetwork(settings.Server) ? this.Request.Host.Host : settings.Server;
            SSHServiceViewModel vm = new SSHServiceViewModel();
            string port = settings.Port == 22 ? "" : $" -p {settings.Port}";
            vm.CommandLine = $"ssh {settings.Username}@{server}{port}";
            vm.Password = settings.Password;
            vm.KeyFilePassword = settings.KeyFilePassword;
            vm.HasKeyFile = !string.IsNullOrEmpty(settings.KeyFile);
            return View(vm);
        }

        [Route("server/theme")]
        public async Task<IActionResult> Theme()
        {
            var data = (await _SettingsRepository.GetSettingAsync<ThemeSettings>()) ?? new ThemeSettings();
            return View(data);
        }
        [Route("server/theme")]
        [HttpPost]
        public async Task<IActionResult> Theme(ThemeSettings settings)
        {
            await _SettingsRepository.UpdateSetting(settings);
            TempData["StatusMessage"] = "Theme settings updated successfully";
            return View(settings);
        }


        [Route("server/emails")]
        public async Task<IActionResult> Emails()
        {
            var data = (await _SettingsRepository.GetSettingAsync<EmailSettings>()) ?? new EmailSettings();
            return View(new EmailsViewModel() { Settings = data });
        }

        [Route("server/emails")]
        [HttpPost]
        public async Task<IActionResult> Emails(EmailsViewModel model, string command)
        {
            if (!model.Settings.IsComplete())
            {
                model.StatusMessage = "Error: Required fields missing";
                return View(model);
            }

            if (command == "Test")
            {
                try
                {
                    var client = model.Settings.CreateSmtpClient();
                    await client.SendMailAsync(model.Settings.From, model.TestEmail, "BTCPay test", "BTCPay test");
                    model.StatusMessage = "Email sent to " + model.TestEmail + ", please, verify you received it";
                }
                catch (Exception ex)
                {
                    model.StatusMessage = "Error: " + ex.Message;
                }
                return View(model);
            }
            else // if(command == "Save")
            {
                await _SettingsRepository.UpdateSetting(model.Settings);
                model.StatusMessage = "Email settings saved";
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

            if (string.IsNullOrEmpty(_Options.LogFile))
            {
                vm.StatusMessage = "Error: File Logging Option not specified. " +
                                   "You need to set debuglog and optionally " +
                                   "debugloglevel in the configuration or through runtime arguments";
            }
            else
            {
                var di = Directory.GetParent(_Options.LogFile);
                if (di == null)
                {
                    vm.StatusMessage = "Error: Could not load log files";
                }

                var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(_Options.LogFile);
                var fileExtension = Path.GetExtension(_Options.LogFile) ?? string.Empty;
                var logFiles = di.GetFiles($"{fileNameWithoutExtension}*{fileExtension}");
                vm.LogFileCount = logFiles.Length;
                vm.LogFiles = logFiles
                    .OrderBy(info => info.LastWriteTime)
                    .Skip(offset)
                    .Take(5)
                    .ToList();
                vm.LogFileOffset = offset;

                if (string.IsNullOrEmpty(file))
                    return View("Logs", vm);
                vm.Log = "";
                var path = Path.Combine(di.FullName, file);
                try
                {
                    using (var fileStream = new FileStream(
                        path,
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
