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
using BTCPayServer.Abstractions;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Configuration;
using BTCPayServer.Data;
using BTCPayServer.HostedServices;
using BTCPayServer.Logging;
using BTCPayServer.Models.ServerViewModels;
using BTCPayServer.Models.StoreViewModels;
using BTCPayServer.Services;
using BTCPayServer.Services.Apps;
using BTCPayServer.Plugins.Emails.Services;
using BTCPayServer.Services.Stores;
using BTCPayServer.Storage.Services;
using BTCPayServer.Storage.Services.Providers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Localization;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using NBitcoin;
using NBitcoin.DataEncoders;
using Renci.SshNet;
using AuthenticationSchemes = BTCPayServer.Abstractions.Constants.AuthenticationSchemes;

namespace BTCPayServer.Controllers
{
    [Authorize(Policy = Client.Policies.CanModifyServerSettings,
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
        private readonly CallbackGenerator _callbackGenerator;
        private readonly UriResolver _uriResolver;
        private readonly TransactionLinkProviders _transactionLinkProviders;
        private readonly LocalizerService _localizer;
        private readonly EmailSenderFactory _emailSenderFactory;
        public IStringLocalizer StringLocalizer { get; }
        public ViewLocalizer ViewLocalizer { get; }

        public UIServerController(
            UserManager<ApplicationUser> userManager,
            UserService userService,
            StoredFileRepository storedFileRepository,
            IFileService fileService,
            EmailSenderFactory emailSenderFactory,
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
            CallbackGenerator callbackGenerator,
            UriResolver uriResolver,
            IHostApplicationLifetime applicationLifetime,
            IHtmlHelper html,
            TransactionLinkProviders transactionLinkProviders,
            LocalizerService localizer,
            IStringLocalizer stringLocalizer,
            ViewLocalizer viewLocalizer,
            BTCPayServerEnvironment environment
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
            _emailSenderFactory = emailSenderFactory;
            _callbackGenerator = callbackGenerator;
            _uriResolver = uriResolver;
            ApplicationLifetime = applicationLifetime;
            Html = html;
            _transactionLinkProviders = transactionLinkProviders;
            _localizer = localizer;
            Environment = environment;
            StringLocalizer = stringLocalizer;
            ViewLocalizer = viewLocalizer;
        }

        [HttpGet("server/stores")]
        public async Task<IActionResult> ListStores()
        {
            var stores = await _StoreRepository.GetStores();
            var vm = new ListStoresViewModel
            {
                Stores = stores
                    .Select(s => new ListStoresViewModel.StoreViewModel
                    {
                        StoreId = s.Id,
                        StoreName = s.StoreName,
                        Archived = s.Archived,
                        Users = s.UserStores
                    })
                    .OrderBy(s => !s.Archived)
                    .ToList()
            };
            return View(vm);
        }

        [HttpGet("server/maintenance")]
        public IActionResult Maintenance()
        {
            var vm = new MaintenanceViewModel
            {
                CanUseSSH = _sshState.CanUseSSH,
                DNSDomain = Request.Host.Host
            };

            if (!vm.CanUseSSH)
                TempData[WellKnownTempData.ErrorMessage] = StringLocalizer["Maintenance feature requires access to SSH properly configured in BTCPay Server configuration."].Value;
            if (IPAddress.TryParse(vm.DNSDomain, out var unused))
                vm.DNSDomain = null;

            return View(vm);
        }

        [HttpPost("server/maintenance")]
        public async Task<IActionResult> Maintenance(MaintenanceViewModel vm, string command)
        {
            vm.CanUseSSH = _sshState.CanUseSSH;
            if (command != "soft-restart" && !vm.CanUseSSH)
            {
                TempData[WellKnownTempData.ErrorMessage] = StringLocalizer["Maintenance feature requires access to SSH properly configured in BTCPay Server configuration."].Value;
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

                var error = await RunSSH(vm, $"changedomain.sh {vm.DNSDomain}");
                if (error != null)
                    return error;

                builder.Path = null;
                builder.Query = null;
                TempData[WellKnownTempData.SuccessMessage] = StringLocalizer["Domain name changing... the server will restart, please use \"{0}\" (this page won't reload automatically)", builder.Uri.AbsoluteUri].Value;
            }
            else if (command == "update")
            {
                var error = await RunSSH(vm, $"btcpay-update.sh");
                if (error != null)
                    return error;
                TempData[WellKnownTempData.SuccessMessage] = StringLocalizer["The server might restart soon if an update is available... (this page won't reload automatically)"].Value;
            }
            else if (command == "clean")
            {
                var error = await RunSSH(vm, $"btcpay-clean.sh");
                if (error != null)
                    return error;
                TempData[WellKnownTempData.SuccessMessage] = StringLocalizer["The old docker images will be cleaned soon..."].Value;
            }
            else if (command == "restart")
            {
                var error = await RunSSH(vm, $"btcpay-restart.sh");
                if (error != null)
                    return error;
                Logs.PayServer.LogInformation("A hard restart has been requested");
                TempData[WellKnownTempData.SuccessMessage] = StringLocalizer["BTCPay will restart momentarily."].Value;
            }
            else if (command == "soft-restart")
            {
                TempData[WellKnownTempData.SuccessMessage] = StringLocalizer["BTCPay will restart momentarily."].Value;
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
        public BTCPayServerEnvironment Environment { get; }

        [Route("server/policies")]
        public async Task<IActionResult> Policies()
        {
            await UpdateViewBag();
            return View(_policiesSettings);
        }

        private async Task UpdateViewBag()
        {
            ViewBag.UpdateUrlPresent = _Options.UpdateUrl != null;
            ViewBag.AppsList = await GetAppSelectList();
            ViewBag.LangDictionaries = await GetLangDictionariesSelectList();
        }

        [HttpPost("server/policies")]
        public async Task<IActionResult> Policies([FromServices] BTCPayNetworkProvider btcPayNetworkProvider, PoliciesSettings settings, string command = "")
        {
            await UpdateViewBag();

            if (command == "ResetTemplate")
            {
                ModelState.Clear();
                await _StoreRepository.SetDefaultStoreTemplate(null);
                this.TempData.SetStatusSuccess(StringLocalizer["Store template successfully unset"]);
                return RedirectToAction(nameof(Policies));
            }

            if (command == "SetTemplate")
            {
                ModelState.Clear();
                var storeId = this.HttpContext.GetStoreData()?.Id;
                if (storeId is null)
                {
                    this.TempData.SetStatusMessageModel(new()
                    {
                        Severity = StatusMessageModel.StatusSeverity.Error,
                        Message = StringLocalizer["You need to select a store first"]
                    });
                }
                else
                {
                    await _StoreRepository.SetDefaultStoreTemplate(storeId, GetUserId());
                    this.TempData.SetStatusSuccess(StringLocalizer["Store template created from store '{0}'. New stores will inherit these settings.", HttpContext.GetStoreData().StoreName]);
                }
                return RedirectToAction(nameof(Policies));
            }

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
                                            .Where(tuple => _transactionLinkProviders.GetDefaultBlockExplorerLink(tuple.PaymentMethodId) != tuple.Link)
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
            if (_policiesSettings.LangDictionary != settings.LangDictionary)
                await _localizer.Load();
            TempData[WellKnownTempData.SuccessMessage] = StringLocalizer["Policies updated successfully"].Value;
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

        private async Task<List<SelectListItem>> GetLangDictionariesSelectList()
        {
            var dictionaries = await this._localizer.GetDictionaries();
            return dictionaries.Select(d => new SelectListItem(d.DictionaryName, d.DictionaryName)).OrderBy(d => d.Value).ToList();
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
                TempData[WellKnownTempData.ErrorMessage] = StringLocalizer["{0} is not fully synched", cryptoCode].Value;
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
                            TempData[WellKnownTempData.ErrorMessage] = StringLocalizer["The access key of the service is not set"].Value;
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
            return View("Confirm", new ConfirmModel(StringLocalizer["Delete LND seed"], StringLocalizer["This action will permanently delete your LND seed and password. You will not be able to recover them if you don't have a backup. Are you sure?"], StringLocalizer["Delete"]));
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
                TempData[WellKnownTempData.ErrorMessage] = StringLocalizer["File with wallet password and seed info not present"].Value;
                return RedirectToAction(nameof(Services));
            }

            if (string.IsNullOrEmpty(model.Seed))
            {
                TempData[WellKnownTempData.ErrorMessage] = StringLocalizer["Seed information was already removed"].Value;
                return RedirectToAction(nameof(Services));
            }

            if (await model.RemoveSeedAndWrite(service.ConnectionString.CookieFilePath))
            {
                TempData[WellKnownTempData.SuccessMessage] = StringLocalizer["Seed successfully removed"].Value;
                return RedirectToAction(nameof(Service), new { serviceName, cryptoCode });
            }
            else
            {
                TempData[WellKnownTempData.ErrorMessage] = StringLocalizer["Seed removal failed"].Value;
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
                    model.QRCodeLink = Url.ActionAbsolute(Request, nameof(GetLNDConfig), new { configKey }).ToString();
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
            if (!_dashBoard.IsFullySynched(cryptoCode, out _))
            {
                TempData[WellKnownTempData.ErrorMessage] = StringLocalizer["{0} is not fully synched", cryptoCode].Value;
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
                    TempData[WellKnownTempData.SuccessMessage] = StringLocalizer["The Dynamic DNS has been successfully queried, your configuration is saved"].Value;
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
                TempData[WellKnownTempData.SuccessMessage] = StringLocalizer["The Dynamic DNS service has been disabled"].Value;
                viewModel.Settings.LastUpdated = null;
            }
            else
            {
                string errorMessage = await viewModel.Settings.SendUpdateRequest(HttpClientFactory.CreateClient());
                if (errorMessage == null)
                {
                    TempData[WellKnownTempData.SuccessMessage] = StringLocalizer["The Dynamic DNS has been successfully queried, your configuration is saved"].Value;
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
                    $"Deleting the dynamic DNS service for <strong>{Html.Encode(hostname)}</strong> means your BTCPay Server will stop updating the associated DNS record periodically.", StringLocalizer["Delete"]));
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
            TempData[WellKnownTempData.SuccessMessage] = StringLocalizer["Dynamic DNS service successfully removed"].Value;
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
                        TempData[WellKnownTempData.SuccessMessage] = StringLocalizer["authorized_keys has been updated"].Value;
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
                    TempData[WellKnownTempData.SuccessMessage] = StringLocalizer["authorized_keys has been updated"].Value;
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
            return View("Confirm", new ConfirmModel(StringLocalizer["Disable modification of SSH settings"], StringLocalizer["This action is permanent and will remove the ability to change the SSH settings via the BTCPay Server user interface."], StringLocalizer["Disable"]));
        }

        [HttpPost("server/services/ssh/disable")]
        public async Task<IActionResult> SSHServiceDisablePost()
        {
            var policies = await _SettingsRepository.GetSettingAsync<PoliciesSettings>() ?? new PoliciesSettings();
            policies.DisableSSHService = true;
            await _SettingsRepository.UpdateSetting(policies);
            TempData[WellKnownTempData.SuccessMessage] = StringLocalizer["Changes to the SSH settings are now permanently disabled in the BTCPay Server user interface"].Value;
            return RedirectToAction(nameof(Services));
        }

        [HttpGet("server/branding")]
        public async Task<IActionResult> Branding()
        {
            var server = await _SettingsRepository.GetSettingAsync<ServerSettings>() ?? new ServerSettings();
            var theme = await _SettingsRepository.GetSettingAsync<ThemeSettings>() ?? new ThemeSettings();

            var vm = new BrandingViewModel
            {
                ServerName = server.ServerName,
                BaseUrl = server.BaseUrl,
                ContactUrl = server.ContactUrl,
                CustomTheme = theme.CustomTheme,
                CustomThemeExtension = theme.CustomThemeExtension,
                CustomThemeCssUrl = await _uriResolver.Resolve(Request.GetAbsoluteRootUri(), theme.CustomThemeCssUrl),
                LogoUrl = await _uriResolver.Resolve(Request.GetAbsoluteRootUri(), theme.LogoUrl)
            };
            return View(vm);
        }

        [HttpPost("server/branding")]
        public async Task<IActionResult> Branding(
            BrandingViewModel vm,
            [FromForm] bool RemoveLogoFile,
            [FromForm] bool RemoveCustomThemeFile,
            [FromForm] string? command = null)
        {
            if (command is "SetBaseUrl")
                vm.BaseUrl = HttpContext.Request.GetRequestBaseUrl().ToString();
            if (string.IsNullOrEmpty(vm.BaseUrl))
            {
                vm.BaseUrl = null;
            }
            else
            {
                if (!RequestBaseUrl.TryFromUrl(vm.BaseUrl, out var baseUrl))
                    ModelState.AddModelError(nameof(vm.BaseUrl), StringLocalizer["Invalid Base URL"]);
                vm.BaseUrl = baseUrl?.ToString();
                vm.BaseUrl = vm.BaseUrl?.WithoutEndingSlash();
            }

            if (!ModelState.IsValid)
                return View(vm);
            var settingsChanged = false;
            var server = await _SettingsRepository.GetSettingAsync<ServerSettings>() ?? new ServerSettings();
            var theme = await _SettingsRepository.GetSettingAsync<ThemeSettings>() ?? new ThemeSettings();

            var userId = GetUserId();
            if (userId is null)
                return NotFound();

            vm.LogoUrl = await _uriResolver.Resolve(this.Request.GetAbsoluteRootUri(), theme.LogoUrl);
            vm.CustomThemeCssUrl = await _uriResolver.Resolve(this.Request.GetAbsoluteRootUri(), theme.CustomThemeCssUrl);

            if (server.ServerName != vm.ServerName)
            {
                server.ServerName = vm.ServerName;
                settingsChanged = true;
            }

            if (server.ContactUrl != vm.ContactUrl)
            {
                server.ContactUrl = !string.IsNullOrWhiteSpace(vm.ContactUrl)
                    ? vm.ContactUrl.IsValidEmail() ? $"mailto:{vm.ContactUrl}" : vm.ContactUrl
                    : null;
                settingsChanged = true;
            }
            if (server.BaseUrl != vm.BaseUrl)
            {
                server.BaseUrl = vm.BaseUrl;
                settingsChanged = true;
            }

            if (settingsChanged)
            {
                await _SettingsRepository.UpdateSetting(server);
            }

            if (vm.CustomThemeFile != null)
            {
                if (vm.CustomThemeFile.ContentType.Equals("text/css", StringComparison.InvariantCulture))
                {
                    // add new file
                    try
                    {
                        var storedFile = await _fileService.AddFile(vm.CustomThemeFile, userId);
                        theme.CustomThemeCssUrl = new UnresolvedUri.FileIdUri(storedFile.Id);
                        vm.CustomThemeCssUrl = await _uriResolver.Resolve(Request.GetAbsoluteRootUri(), theme.CustomThemeCssUrl);
                        settingsChanged = true;
                    }
                    catch (Exception e)
                    {
                        ModelState.AddModelError(nameof(vm.CustomThemeFile), StringLocalizer["Could not save CSS file: {0}", e.Message]);
                    }
                }
                else
                {
                    ModelState.AddModelError(nameof(vm.CustomThemeFile), StringLocalizer["The uploaded file needs to be a CSS file"]);
                }
            }
            else if (RemoveCustomThemeFile && theme.CustomThemeCssUrl is not null)
            {
                vm.CustomThemeCssUrl = null;
                theme.CustomThemeCssUrl = null;
                theme.CustomTheme = false;
                theme.CustomThemeExtension = ThemeExtension.Custom;
                settingsChanged = true;
            }

            if (vm.LogoFile != null)
            {
                if (vm.LogoFile.Length > 1_000_000)
                {
                    ModelState.AddModelError(nameof(vm.LogoFile), StringLocalizer["The uploaded file should be less than {0}", "1MB"]);
                }
                else if (!vm.LogoFile.ContentType.StartsWith("image/", StringComparison.InvariantCulture))
                {
                    ModelState.AddModelError(nameof(vm.LogoFile), StringLocalizer["The uploaded file needs to be an image"]);
                }
                else
                {
                    var formFile = await vm.LogoFile.Bufferize();
                    if (!FileTypeDetector.IsPicture(formFile.Buffer, formFile.FileName))
                    {
                        ModelState.AddModelError(nameof(vm.LogoFile), StringLocalizer["The uploaded file needs to be an image"]);
                    }
                    else
                    {
                        vm.LogoFile = formFile;
                        // add new file
                        try
                        {
                            var storedFile = await _fileService.AddFile(vm.LogoFile, userId);
                            theme.LogoUrl = new UnresolvedUri.FileIdUri(storedFile.Id);
                            vm.LogoUrl = await _uriResolver.Resolve(Request.GetAbsoluteRootUri(), theme.LogoUrl);
                            settingsChanged = true;
                        }
                        catch (Exception e)
                        {
                            ModelState.AddModelError(nameof(vm.LogoFile), StringLocalizer["Could not save logo: {0}", e.Message]);
                        }
                    }
                }
            }
            else if (RemoveLogoFile && theme.LogoUrl is not null)
            {
                vm.LogoUrl = null;
                theme.LogoUrl = null;
                settingsChanged = true;
            }

            if (vm.CustomTheme && theme.CustomThemeExtension != vm.CustomThemeExtension)
            {
                // Require a custom theme to be defined in that case
                if (string.IsNullOrEmpty(vm.CustomThemeCssUrl) && theme.CustomThemeCssUrl is null)
                {
                    ModelState.AddModelError(nameof(vm.CustomThemeCssUrl), "Please provide a custom theme");
                }
                else
                {
                    theme.CustomThemeExtension = vm.CustomThemeExtension;
                    settingsChanged = true;
                }
            }

            if (theme.CustomTheme != vm.CustomTheme && !RemoveCustomThemeFile)
            {
                theme.CustomTheme = vm.CustomTheme;
                settingsChanged = true;
            }

            if (settingsChanged)
            {
                await _SettingsRepository.UpdateSetting(theme);
                TempData[WellKnownTempData.SuccessMessage] = StringLocalizer["Settings updated successfully"].Value;
                return RedirectToAction(nameof(Branding));
            }

            return View(vm);
        }

        [Route("server/logs/{file?}")]
        public async Task<IActionResult> LogsView(string? file = null, int offset = 0, bool download = false)
        {
            if (offset < 0)
            {
                offset = 0;
            }

            var vm = new LogsViewModel();

            if (string.IsNullOrEmpty(_Options.LogFile))
            {
                TempData[WellKnownTempData.ErrorMessage] = StringLocalizer["File Logging Option not specified. You need to set debuglog and optionally debugloglevel in the configuration or through runtime arguments"].Value;
            }
            else
            {
                var di = Directory.GetParent(_Options.LogFile);
                if (di is null)
                {
                    TempData[WellKnownTempData.ErrorMessage] = StringLocalizer["Could not load log files"].Value;
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
                    var fileStream = new FileStream(
                        fi.FullName,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite);
                    if (download)
                    {
                        return new FileStreamResult(fileStream, "text/plain")
                        {
                            FileDownloadName = file
                        };
                    }
                    await using (fileStream)
                    {
                        using var reader = new StreamReader(fileStream);
                        vm.Log = await reader.ReadToEndAsync();
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
