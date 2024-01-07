#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Configuration;
using BTCPayServer.Data;
using BTCPayServer.HostedServices;
using BTCPayServer.Hosting;
using BTCPayServer.Logging;
using BTCPayServer.Models;
using BTCPayServer.Models.ServerViewModels;
using BTCPayServer.Payments;
using BTCPayServer.Services;
using BTCPayServer.Services.Apps;
using BTCPayServer.Services.Mails;
using BTCPayServer.Services.Stores;
using BTCPayServer.Storage.Models;
using BTCPayServer.Storage.Services;
using BTCPayServer.Storage.Services.Providers;
using BTCPayServer.Validation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using NBitcoin;
using NBitcoin.DataEncoders;
using Renci.SshNet;
using AuthenticationSchemes = BTCPayServer.Abstractions.Constants.AuthenticationSchemes;

namespace BTCPayServer.Controllers
{
    [Authorize(Policy = BTCPayServer.Client.Policies.CanModifyServerSettings,
               AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public partial class UIServerController : Controller
    {
        private readonly UserManager<ApplicationUser> _UserManager;
        private readonly UserService _userService;
        readonly SettingsRepository _SettingsRepository;
        readonly PoliciesSettings _policiesSettings;
        private readonly NBXplorerDashboard _dashBoard;
        private readonly StoreRepository _StoreRepository;
        readonly LightningConfigurationProvider _LnConfigProvider;
        private readonly TorServices _torServices;
        private readonly BTCPayServerOptions _Options;
        private readonly AppService _AppService;
        private readonly CheckConfigurationHostedService _sshState;
        private readonly EventAggregator _eventAggregator;
        private readonly IOptions<ExternalServicesOptions> _externalServiceOptions;
        private readonly Logs Logs;
        private readonly StoredFileRepository _StoredFileRepository;
        private readonly IFileService _fileService;
        private readonly IEnumerable<IStorageProviderService> _StorageProviderServices;
        private readonly LinkGenerator _linkGenerator;
        private readonly EmailSenderFactory _emailSenderFactory;
        private readonly TransactionLinkProviders _transactionLinkProviders;

        public UIServerController(
            UserManager<ApplicationUser> userManager,
            UserService userService,
            StoredFileRepository storedFileRepository,
            IFileService fileService,
            IEnumerable<IStorageProviderService> storageProviderServices,
            BTCPayServerOptions options,
            SettingsRepository settingsRepository,
            PoliciesSettings policiesSettings,
            NBXplorerDashboard dashBoard,
            IHttpClientFactory httpClientFactory,
            LightningConfigurationProvider lnConfigProvider,
            TorServices torServices,
            StoreRepository storeRepository,
            AppService appService,
            CheckConfigurationHostedService sshState,
            EventAggregator eventAggregator,
            IOptions<ExternalServicesOptions> externalServiceOptions,
            Logs logs,
            LinkGenerator linkGenerator,
            EmailSenderFactory emailSenderFactory,
            IHostApplicationLifetime applicationLifetime,
            IHtmlHelper html,
            TransactionLinkProviders transactionLinkProviders
        )
        {
            _policiesSettings = policiesSettings;
            _Options = options;
            _StoredFileRepository = storedFileRepository;
            _fileService = fileService;
            _StorageProviderServices = storageProviderServices;
            _UserManager = userManager;
            _userService = userService;
            _SettingsRepository = settingsRepository;
            _dashBoard = dashBoard;
            HttpClientFactory = httpClientFactory;
            _StoreRepository = storeRepository;
            _LnConfigProvider = lnConfigProvider;
            _torServices = torServices;
            _AppService = appService;
            _sshState = sshState;
            _eventAggregator = eventAggregator;
            _externalServiceOptions = externalServiceOptions;
            Logs = logs;
            _linkGenerator = linkGenerator;
            _emailSenderFactory = emailSenderFactory;
            ApplicationLifetime = applicationLifetime;
            Html = html;
            _transactionLinkProviders = transactionLinkProviders;
        }

        [Route("server/maintenance")]
        public IActionResult Maintenance()
        {
            MaintenanceViewModel vm = new MaintenanceViewModel();
            vm.CanUseSSH = _sshState.CanUseSSH;
            if (!vm.CanUseSSH)
                TempData[WellKnownTempData.ErrorMessage] = "Maintenance feature requires access to SSH properly configured in BTCPay Server configuration.";
            vm.DNSDomain = this.Request.Host.Host;
            if (IPAddress.TryParse(vm.DNSDomain, out var unused))
                vm.DNSDomain = null;
            return View(vm);
        }

        [Route("server/maintenance")]
        [HttpPost]
        public async Task<IActionResult> Maintenance(MaintenanceViewModel vm, string command)
        {
            vm.CanUseSSH = _sshState.CanUseSSH;
            if (command != "soft-restart" && !vm.CanUseSSH)
            {
                TempData[WellKnownTempData.ErrorMessage] = "Maintenance feature requires access to SSH properly configured in BTCPay Server configuration.";
                return View(vm);
            }
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
            else if (command == "restart")
            {
                var error = await RunSSH(vm, $"btcpay-restart.sh");
                if (error != null)
                    return error;
                Logs.PayServer.LogInformation("A hard restart has been requested");
                TempData[WellKnownTempData.SuccessMessage] = $"BTCPay will restart momentarily.";
            }
            else if (command == "soft-restart")
            {
                TempData[WellKnownTempData.SuccessMessage] = $"BTCPay will restart momentarily.";
                Logs.PayServer.LogInformation("A soft restart has been requested");
                _ = Task.Delay(3000).ContinueWith((t) => ApplicationLifetime.StopApplication());
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
        public IActionResult SeeRunId(string? expected = null)
        {
            if (expected == RunId)
                return Ok();
            return BadRequest();
        }

        private async Task<IActionResult?> RunSSH(MaintenanceViewModel vm, string command)
        {
            SshClient? sshClient = null;

            try
            {
                sshClient = await _Options.SSHSettings.ConnectAsync();
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

        private async Task RunSSHCore(SshClient sshClient, string ssh)
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

        public IHttpClientFactory HttpClientFactory { get; }
        public IHostApplicationLifetime ApplicationLifetime { get; }
        public IHtmlHelper Html { get; }

        [Route("server/policies")]
        public async Task<IActionResult> Policies()
        {
            ViewBag.AppsList = await GetAppSelectList();
            ViewBag.UpdateUrlPresent = _Options.UpdateUrl != null;
            return View(_policiesSettings);
        }

        [HttpPost("server/policies")]
        public async Task<IActionResult> Policies([FromServices] BTCPayNetworkProvider btcPayNetworkProvider, PoliciesSettings settings, string command = "")
        {
            ViewBag.UpdateUrlPresent = _Options.UpdateUrl != null;
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
            settings.BlockExplorerLinks = settings.BlockExplorerLinks
                                            .Where(tuple => _transactionLinkProviders.GetDefaultBlockExplorerLink(PaymentMethodId.Parse(tuple.CryptoCode)) != tuple.Link)
                                            .Where(tuple => tuple.Link is not null)
                                            .ToList();

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
                var apps = (await _AppService.GetApps(appIdsToFetch.ToArray()))
                    .ToDictionary(data => data.Id, data => data.AppType);
                ;
                if (!string.IsNullOrEmpty(settings.RootAppId))
                {
                    settings.RootAppType = apps[settings.RootAppId];
                }

                foreach (var domainToAppMappingItem in settings.DomainToAppMapping)
                {
                    domainToAppMappingItem.AppType = apps[domainToAppMappingItem.AppId];
                }
            }

            await _SettingsRepository.UpdateSetting(settings);
            _ = _transactionLinkProviders.RefreshTransactionLinkTemplates();
            TempData[WellKnownTempData.SuccessMessage] = "Policies updated successfully";
            return RedirectToAction(nameof(Policies));
        }

        [Route("server/services")]
        public IActionResult Services()
        {
            var result = new ServicesViewModel { ExternalServices = _externalServiceOptions.Value.ExternalServices.ToList() };

            // other services
            foreach (var externalService in _externalServiceOptions.Value.OtherExternalServices)
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

            return View(result);
        }

        private async Task<List<SelectListItem>> GetAppSelectList()
        {
            var types = _AppService.GetAvailableAppTypes();
            var apps = (await _AppService.GetAllApps(null, true))
                .Select(a =>
                    new SelectListItem($"{types[a.AppType]} - {a.AppName} - {a.StoreName}", a.Id)).ToList();
            apps.Insert(0, new SelectListItem("(None)", null));
            return apps;
        }

        private static bool TryParseAsExternalService(TorService torService, [MaybeNullWhen(false)] out ExternalService externalService)
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

        private ExternalService? GetService(string serviceName, string cryptoCode)
        {
            var result = _externalServiceOptions.Value.ExternalServices.GetService(serviceName, cryptoCode);
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

        [Route("server/services/{serviceName}/{cryptoCode?}")]
        public async Task<IActionResult> Service(string serviceName, string cryptoCode, bool showQR = false, ulong? nonce = null)
        {
            var service = GetService(serviceName, cryptoCode);
            if (service == null)
                return NotFound();
            if (!string.IsNullOrEmpty(cryptoCode) && !_dashBoard.IsFullySynched(cryptoCode, out _) && service.Type != ExternalServiceTypes.RPC)
            {
                TempData[WellKnownTempData.ErrorMessage] = $"{cryptoCode} is not fully synched";
                return RedirectToAction(nameof(Services));
            }
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
                var connectionString = await service.ConnectionString.Expand(this.Request.GetAbsoluteUriNoPathBase(), service.Type, _Options.NetworkType);
                switch (service.Type)
                {
                    case ExternalServiceTypes.Charge:
                        return LightningChargeServices(service, connectionString, showQR);
                    case ExternalServiceTypes.RTL:
                    case ExternalServiceTypes.ThunderHub:
                    case ExternalServiceTypes.Spark:
                    case ExternalServiceTypes.Torq:
                        if (connectionString.AccessKey == null)
                        {
                            TempData[WellKnownTempData.ErrorMessage] = $"The access key of the service is not set";
                            return RedirectToAction(nameof(Services));
                        }
                        LightningWalletServices vm = new LightningWalletServices();
                        vm.ShowQR = showQR;
                        vm.WalletName = service.DisplayName;
                        string tokenParam = "access-key";
                        if (service.Type == ExternalServiceTypes.ThunderHub)
                            tokenParam = "token";
                        vm.ServiceLink = $"{connectionString.Server}?{tokenParam}={connectionString.AccessKey}";
                        return View("LightningWalletServices", vm);
                    case ExternalServiceTypes.CLightningRest:
                        return LndServices(service, connectionString, nonce, "CLightningRestServices");
                    case ExternalServiceTypes.LNDGRPC:
                    case ExternalServiceTypes.LNDRest:
                        return LndServices(service, connectionString, nonce);
                    case ExternalServiceTypes.Configurator:
                        return View("ConfiguratorService",
                            new LightningWalletServices()
                            {
                                ShowQR = showQR,
                                WalletName = service.ServiceName,
                                ServiceLink = $"{connectionString.Server}?password={connectionString.AccessKey}"
                            });
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

        [HttpGet("server/services/{serviceName}/{cryptoCode}/removelndseed")]
        public IActionResult RemoveLndSeed(string serviceName, string cryptoCode)
        {
            return View("Confirm", new ConfirmModel("Delete LND seed", "This action will permanently delete your LND seed and password. You will not be able to recover them if you don't have a backup. Are you sure?", "Delete"));
        }

        [HttpPost("server/services/{serviceName}/{cryptoCode}/removelndseed")]
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

        private IActionResult LndServices(ExternalService service, ExternalConnectionString connectionString, ulong? nonce, string view = nameof(LndServices))
        {
            var model = new LndServicesViewModel();
            if (service.Type == ExternalServiceTypes.LNDGRPC)
            {
                model.Host = $"{connectionString.Server.DnsSafeHost}:{connectionString.Server.Port}";
                model.SSL = connectionString.Server.Scheme == "https";
                model.ConnectionType = "GRPC";
                model.GRPCSSLCipherSuites = "ECDHE-RSA-AES128-GCM-SHA256:ECDHE-RSA-AES256-GCM-SHA384:ECDHE-ECDSA-AES128-SHA256";
            }
            else if (service.Type == ExternalServiceTypes.LNDRest || service.Type == ExternalServiceTypes.CLightningRest)
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

            return View(view, model);
        }

        private static ulong GetConfigKey(string type, string serviceName, string cryptoCode, ulong nonce)
        {
            return ((ulong)(uint)HashCode.Combine(type, serviceName, cryptoCode, nonce) | (nonce & 0xffffffff00000000UL));
        }

        [Route("lnd-config/{configKey}/lnd.config")]
        [AllowAnonymous]
        [EnableCors(CorsPolicies.All)]
        [IgnoreAntiforgeryToken]
        public IActionResult GetLNDConfig(ulong configKey)
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
                TempData[WellKnownTempData.ErrorMessage] = $"{cryptoCode} is not fully synched";
                return RedirectToAction(nameof(Services));
            }
            var service = GetService(serviceName, cryptoCode);
            if (service == null)
                return NotFound();

            ExternalConnectionString? connectionString = null;
            try
            {
                connectionString = await service.ConnectionString.Expand(this.Request.GetAbsoluteUriNoPathBase(), service.Type, _Options.NetworkType);
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
            else if (service.Type == ExternalServiceTypes.LNDRest || service.Type == ExternalServiceTypes.CLightningRest)
            {
                var restconf = new LNDRestConfiguration();
                restconf.Type = service.Type == ExternalServiceTypes.LNDRest ? "lnd-rest" : "clightning-rest";
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

            var nonce = RandomUtils.GetUInt64();
            var configKey = GetConfigKey("lnd", serviceName, cryptoCode, nonce);
            _LnConfigProvider.KeepConfig(configKey, confs);
            return RedirectToAction(nameof(Service), new { cryptoCode = cryptoCode, serviceName = serviceName, nonce = nonce });
        }

        [Route("server/services/dynamic-dns")]
        public async Task<IActionResult> DynamicDnsServices()
        {
            var settings = (await _SettingsRepository.GetSettingAsync<DynamicDnsSettings>()) ?? new DynamicDnsSettings();
            return View(settings.Services.Select(s => new DynamicDnsViewModel()
            {
                Settings = s
            }).ToArray());
        }
        [Route("server/services/dynamic-dns/{hostname}")]
        public async Task<IActionResult> DynamicDnsServices(string hostname)
        {
            var settings = (await _SettingsRepository.GetSettingAsync<DynamicDnsSettings>()) ?? new DynamicDnsSettings();
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
        public async Task<IActionResult> DynamicDnsService(DynamicDnsViewModel viewModel, string? command = null)
        {
            if (!ModelState.IsValid)
            {
                return View(viewModel);
            }
            if (command == "Save")
            {
                var settings = (await _SettingsRepository.GetSettingAsync<DynamicDnsSettings>()) ?? new DynamicDnsSettings();
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
                    await _SettingsRepository.UpdateSetting(settings);
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
        public async Task<IActionResult> DynamicDnsService(DynamicDnsViewModel viewModel, string hostname, string? command = null)
        {
            if (!ModelState.IsValid)
            {
                return View(viewModel);
            }
            var settings = (await _SettingsRepository.GetSettingAsync<DynamicDnsSettings>()) ?? new DynamicDnsSettings();

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
            await _SettingsRepository.UpdateSetting(settings);
            this.RouteData.Values.Remove(nameof(hostname));
            return RedirectToAction(nameof(DynamicDnsServices));
        }

        [HttpGet("server/services/dynamic-dns/{hostname}/delete")]
        public async Task<IActionResult> DeleteDynamicDnsService(string hostname)
        {
            var settings = await _SettingsRepository.GetSettingAsync<DynamicDnsSettings>() ?? new DynamicDnsSettings();
            var i = settings.Services.FindIndex(d => d.Hostname.Equals(hostname, StringComparison.OrdinalIgnoreCase));
            if (i == -1)
                return NotFound();
            return View("Confirm",
                new ConfirmModel("Delete dynamic DNS service",
                    $"Deleting the dynamic DNS service for <strong>{Html.Encode(hostname)}</strong> means your BTCPay Server will stop updating the associated DNS record periodically.", "Delete"));
        }

        [HttpPost("server/services/dynamic-dns/{hostname}/delete")]
        public async Task<IActionResult> DeleteDynamicDnsServicePost(string hostname)
        {
            var settings = (await _SettingsRepository.GetSettingAsync<DynamicDnsSettings>()) ?? new DynamicDnsSettings();
            var i = settings.Services.FindIndex(d => d.Hostname.Equals(hostname, StringComparison.OrdinalIgnoreCase));
            if (i == -1)
                return NotFound();
            settings.Services.RemoveAt(i);
            await _SettingsRepository.UpdateSetting(settings);
            TempData[WellKnownTempData.SuccessMessage] = "Dynamic DNS service successfully removed";
            RouteData.Values.Remove(nameof(hostname));
            return RedirectToAction(nameof(DynamicDnsServices));
        }

        [HttpGet("server/services/ssh")]
        public async Task<IActionResult> SSHService()
        {
            if (!CanShowSSHService())
                return NotFound();

            var settings = _Options.SSHSettings;
            var server = Extensions.IsLocalNetwork(settings.Server) ? this.Request.Host.Host : settings.Server;
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
                    using var sshClient = await _Options.SSHSettings.ConnectAsync();
                    var result = await sshClient.RunBash("cat ~/.ssh/authorized_keys", TimeSpan.FromSeconds(10));
                    vm.SSHKeyFileContent = result.Output;
                }
                catch { }
            }
            return View(vm);
        }

        bool CanShowSSHService()
        {
            return !_policiesSettings.DisableSSHService &&
                   _Options.SSHSettings != null && (_sshState.CanUseSSH || CanAccessAuthorizedKeyFile());
        }

        private bool CanAccessAuthorizedKeyFile()
        {
            return _Options.SSHSettings?.AuthorizedKeysFile != null && System.IO.File.Exists(_Options.SSHSettings.AuthorizedKeysFile);
        }

        [HttpPost("server/services/ssh")]
        public async Task<IActionResult> SSHService(SSHServiceViewModel viewModel, string? command = null)
        {
            if (!CanShowSSHService())
                return NotFound();

            if (command is "Save")
            {
                string newContent = viewModel?.SSHKeyFileContent ?? string.Empty;
                newContent = newContent.Replace("\r\n", "\n", StringComparison.OrdinalIgnoreCase);

                bool updated = false;
                Exception? exception = null;
                // Let's try to just write the file
                if (CanAccessAuthorizedKeyFile())
                {
                    try
                    {
                        await System.IO.File.WriteAllTextAsync(_Options.SSHSettings.AuthorizedKeysFile, newContent);
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
                        using (var sshClient = await _Options.SSHSettings.ConnectAsync())
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

            if (command is "disable")
            {
                return RedirectToAction(nameof(SSHServiceDisable));
            }

            return NotFound();
        }

        [HttpGet("server/services/ssh/disable")]
        public IActionResult SSHServiceDisable()
        {
            return View("Confirm", new ConfirmModel("Disable modification of SSH settings", "This action is permanent and will remove the ability to change the SSH settings via the BTCPay Server user interface.", "Disable"));
        }

        [HttpPost("server/services/ssh/disable")]
        public async Task<IActionResult> SSHServiceDisablePost()
        {
            var policies = await _SettingsRepository.GetSettingAsync<PoliciesSettings>() ?? new PoliciesSettings();
            policies.DisableSSHService = true;
            await _SettingsRepository.UpdateSetting(policies);
            TempData[WellKnownTempData.SuccessMessage] = "Changes to the SSH settings are now permanently disabled in the BTCPay Server user interface";
            return RedirectToAction(nameof(Services));
        }

        [HttpGet("server/theme")]
        public async Task<IActionResult> Theme()
        {
            var data = await _SettingsRepository.GetSettingAsync<ThemeSettings>() ?? new ThemeSettings();
            return View(data);
        }

        [HttpPost("server/theme")]
        public async Task<IActionResult> Theme(
            ThemeSettings model,
            [FromForm] bool RemoveLogoFile,
            [FromForm] bool RemoveCustomThemeFile)
        {
            var settingsChanged = false;
            var settings = await _SettingsRepository.GetSettingAsync<ThemeSettings>() ?? new ThemeSettings();

            var userId = GetUserId();
            if (userId is null)
                return NotFound();

            if (model.CustomThemeFile != null)
            {
                if (model.CustomThemeFile.ContentType.Equals("text/css", StringComparison.InvariantCulture))
                {
                    // delete existing file
                    if (!string.IsNullOrEmpty(settings.CustomThemeFileId))
                    {
                        await _fileService.RemoveFile(settings.CustomThemeFileId, userId);
                    }

                    // add new file
                    try
                    {
                        var storedFile = await _fileService.AddFile(model.CustomThemeFile, userId);
                        settings.CustomThemeFileId = storedFile.Id;
                        settingsChanged = true;
                    }
                    catch (Exception e)
                    {
                        ModelState.AddModelError(nameof(settings.CustomThemeFile), $"Could not save theme file: {e.Message}");
                    }
                }
                else
                {
                    ModelState.AddModelError(nameof(settings.CustomThemeFile), "The uploaded theme file needs to be a CSS file");
                }
            }
            else if (RemoveCustomThemeFile && !string.IsNullOrEmpty(settings.CustomThemeFileId))
            {
                await _fileService.RemoveFile(settings.CustomThemeFileId, userId);
                settings.CustomThemeFileId = null;
                settingsChanged = true;
            }

            if (model.LogoFile != null)
            {
                if (model.LogoFile.Length > 1_000_000)
                {
                    ModelState.AddModelError(nameof(model.LogoFile), "The uploaded logo file should be less than 1MB");
                }
                else if (!model.LogoFile.ContentType.StartsWith("image/", StringComparison.InvariantCulture))
                {
                    ModelState.AddModelError(nameof(model.LogoFile), "The uploaded logo file needs to be an image");
                }
                else
                {
                    var formFile = await model.LogoFile.Bufferize();
                    if (!FileTypeDetector.IsPicture(formFile.Buffer, formFile.FileName))
                    {
                        ModelState.AddModelError(nameof(model.LogoFile), "The uploaded logo file needs to be an image");
                    }
                    else
                    {
                        model.LogoFile = formFile;
                        // delete existing file
                        if (!string.IsNullOrEmpty(settings.LogoFileId))
                        {
                            await _fileService.RemoveFile(settings.LogoFileId, userId);
                        }
                        // add new file
                        try
                        {
                            var storedFile = await _fileService.AddFile(model.LogoFile, userId);
                            settings.LogoFileId = storedFile.Id;
                            settingsChanged = true;
                        }
                        catch (Exception e)
                        {
                            ModelState.AddModelError(nameof(settings.LogoFile), $"Could not save logo: {e.Message}");
                        }
                    }
                }
            }
            else if (RemoveLogoFile && !string.IsNullOrEmpty(settings.LogoFileId))
            {
                await _fileService.RemoveFile(settings.LogoFileId, userId);
                settings.LogoFileId = null;
                settingsChanged = true;
            }

            if (model.CustomTheme && !string.IsNullOrEmpty(model.CustomThemeCssUri) && !Uri.IsWellFormedUriString(model.CustomThemeCssUri, UriKind.RelativeOrAbsolute))
            {
                ModelState.AddModelError(nameof(settings.CustomThemeCssUri), "Please provide a non-empty theme URI");
            }
            else if (settings.CustomThemeCssUri != model.CustomThemeCssUri)
            {
                settings.CustomThemeCssUri = model.CustomThemeCssUri;
                settingsChanged = true;
            }

            if (settings.CustomThemeExtension != model.CustomThemeExtension)
            {
                // Require a custom theme to be defined in that case
                if (string.IsNullOrEmpty(model.CustomThemeCssUri) && string.IsNullOrEmpty(settings.CustomThemeFileId))
                {
                    ModelState.AddModelError(nameof(settings.CustomThemeFile), "Please provide a custom theme");
                }
                else
                {
                    settings.CustomThemeExtension = model.CustomThemeExtension;
                    settingsChanged = true;
                }
            }

            if (settings.CustomTheme != model.CustomTheme)
            {
                settings.CustomTheme = model.CustomTheme;
                settingsChanged = true;
            }

            if (settingsChanged)
            {
                await _SettingsRepository.UpdateSetting(settings);
                TempData[WellKnownTempData.SuccessMessage] = "Theme settings updated successfully";
            }

            return View(settings);
        }

        [Route("server/emails")]
        public async Task<IActionResult> Emails()
        {
            var data = (await _SettingsRepository.GetSettingAsync<EmailSettings>()) ?? new EmailSettings();
            return View(new EmailsViewModel(data));
        }

        [Route("server/emails")]
        [HttpPost]
        public async Task<IActionResult> Emails(EmailsViewModel model, string command)
        {
            if (command == "Test")
            {
                try
                {
                    if (model.PasswordSet)
                    {
                        var settings = await _SettingsRepository.GetSettingAsync<EmailSettings>() ?? new EmailSettings();
                        model.Settings.Password = settings.Password;
                    }
                    model.Settings.Validate("Settings.", ModelState);
                    if (string.IsNullOrEmpty(model.TestEmail))
                        ModelState.AddModelError(nameof(model.TestEmail), new RequiredAttribute().FormatErrorMessage(nameof(model.TestEmail)));
                    if (!ModelState.IsValid)
                        return View(model);
                    using (var client = await model.Settings.CreateSmtpClient())
                    using (var message = model.Settings.CreateMailMessage(MailboxAddress.Parse(model.TestEmail), "BTCPay test", "BTCPay test", false))
                    {
                        await client.SendAsync(message);
                        await client.DisconnectAsync(true);
                    }
                    TempData[WellKnownTempData.SuccessMessage] = $"Email sent to {model.TestEmail}. Please verify you received it.";
                }
                catch (Exception ex)
                {
                    TempData[WellKnownTempData.ErrorMessage] = ex.Message;
                }
                return View(model);
            }
            if (command == "ResetPassword")
            {
                var settings = await _SettingsRepository.GetSettingAsync<EmailSettings>() ?? new EmailSettings();
                settings.Password = null;
                await _SettingsRepository.UpdateSetting(settings);
                TempData[WellKnownTempData.SuccessMessage] = "Email server password reset";
                return RedirectToAction(nameof(Emails));
            }
            else // if (command == "Save")
            {
                if (model.Settings.From is not null && !MailboxAddressValidator.IsMailboxAddress(model.Settings.From))
                {
                    ModelState.AddModelError("Settings.From", "Invalid email");
                    return View(model);
                }
                var oldSettings = await _SettingsRepository.GetSettingAsync<EmailSettings>() ?? new EmailSettings();
                if (new EmailsViewModel(oldSettings).PasswordSet)
                {
                    model.Settings.Password = oldSettings.Password;
                }
                await _SettingsRepository.UpdateSetting(model.Settings);
                TempData[WellKnownTempData.SuccessMessage] = "Email settings saved";
                return RedirectToAction(nameof(Emails));
            }
        }

        [Route("server/logs/{file?}")]
        public async Task<IActionResult> LogsView(string? file = null, int offset = 0)
        {
            if (offset < 0)
            {
                offset = 0;
            }

            var vm = new LogsViewModel();

            if (string.IsNullOrEmpty(_Options.LogFile))
            {
                TempData[WellKnownTempData.ErrorMessage] = "File Logging Option not specified. " +
                                   "You need to set debuglog and optionally " +
                                   "debugloglevel in the configuration or through runtime arguments";
            }
            else
            {
                var di = Directory.GetParent(_Options.LogFile);
                if (di is null)
                {
                    TempData[WellKnownTempData.ErrorMessage] = "Could not load log files";
                    return View("Logs", vm);
                }

                var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(_Options.LogFile);
                var fileExtension = Path.GetExtension(_Options.LogFile) ?? string.Empty;
                // We are checking if "di" is null above yet accessing GetFiles on it, this could lead to an exception?
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
                    using var fileStream = new FileStream(
                        fi.FullName,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite);
                    using var reader = new StreamReader(fileStream);
                    vm.Log = await reader.ReadToEndAsync();
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
