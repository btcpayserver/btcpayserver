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
using BTCPayServer.Configuration.External;

namespace BTCPayServer.Controllers
{
    [Authorize(Policy = BTCPayServer.Security.Policies.CanModifyServerSettings.Key)]
    public class ServerController : Controller
    {
        private UserManager<ApplicationUser> _UserManager;
        SettingsRepository _SettingsRepository;
        private readonly NBXplorerDashboard _dashBoard;
        private RateFetcher _RateProviderFactory;
        private StoreRepository _StoreRepository;
        LightningConfigurationProvider _LnConfigProvider;
        BTCPayServerOptions _Options;

        public ServerController(UserManager<ApplicationUser> userManager,
            Configuration.BTCPayServerOptions options,
            RateFetcher rateProviderFactory,
            SettingsRepository settingsRepository,
            NBXplorerDashboard dashBoard,
            LightningConfigurationProvider lnConfigProvider,
            Services.Stores.StoreRepository storeRepository)
        {
            _Options = options;
            _UserManager = userManager;
            _SettingsRepository = settingsRepository;
            _dashBoard = dashBoard;
            _RateProviderFactory = rateProviderFactory;
            _StoreRepository = storeRepository;
            _LnConfigProvider = lnConfigProvider;
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
                        var addresses1 = Dns.GetHostAddressesAsync(this.Request.Host.Host);
                        var addresses2 = Dns.GetHostAddressesAsync(vm.DNSDomain);
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
            else
            {
                return NotFound();
            }
            return RedirectToAction(nameof(Maintenance));
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
            var roles = await _UserManager.GetRolesAsync(user);
            var isAdmin = IsAdmin(roles);
            bool updated = false;

            if (isAdmin != viewModel.IsAdmin)
            {
                if (viewModel.IsAdmin)
                    await _UserManager.AddToRoleAsync(user, Roles.ServerAdmin);
                else
                    await _UserManager.RemoveFromRoleAsync(user, Roles.ServerAdmin);
                updated = true;
            }
            if (updated)
            {
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
            return View("Confirm", new ConfirmModel()
            {
                Title = "Delete user " + user.Email,
                Description = "This user will be permanently deleted",
                Action = "Delete"
            });
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

        [Route("server/emails")]
        public async Task<IActionResult> Emails()
        {
            var data = (await _SettingsRepository.GetSettingAsync<EmailSettings>()) ?? new EmailSettings();
            return View(new EmailsViewModel() { Settings = data });
        }

        [Route("server/policies")]
        public async Task<IActionResult> Policies()
        {
            var data = (await _SettingsRepository.GetSettingAsync<PoliciesSettings>()) ?? new PoliciesSettings();
            return View(data);
        }
        [Route("server/policies")]
        [HttpPost]
        public async Task<IActionResult> Policies(PoliciesSettings settings)
        {
            await _SettingsRepository.UpdateSetting(settings);
            TempData["StatusMessage"] = "Policies updated successfully";
            return View(settings);
        }

        [Route("server/services")]
        public IActionResult Services()
        {
            var result = new ServicesViewModel();
            foreach (var cryptoCode in _Options.ExternalServicesByCryptoCode.Keys)
            {
                int i = 0;
                foreach (var grpcService in _Options.ExternalServicesByCryptoCode.GetServices<ExternalLnd>(cryptoCode))
                {
                    result.LNDServices.Add(new ServicesViewModel.LNDServiceViewModel()
                    {
                        Crypto = cryptoCode,
                        Type = grpcService.Type,
                        Index = i++,
                    });
                }
            }
            result.HasSSH = _Options.SSHSettings != null;
            return View(result);
        }

        [Route("server/services/lnd-grpc/{cryptoCode}/{index}")]
        public IActionResult LndGrpcServices(string cryptoCode, int index, uint? nonce)
        {
            if (!_dashBoard.IsFullySynched(cryptoCode, out var unusud))
            {
                StatusMessage = $"Error: {cryptoCode} is not fully synched";
                return RedirectToAction(nameof(Services));
            }
            var external = GetExternalLndConnectionString(cryptoCode, index);
            if (external == null)
                return NotFound();
            var model = new LndGrpcServicesViewModel();

            model.Host = $"{external.BaseUri.DnsSafeHost}:{external.BaseUri.Port}";
            model.SSL = external.BaseUri.Scheme == "https";
            if (external.CertificateThumbprint != null)
            {
                model.CertificateThumbprint = Encoders.Hex.EncodeData(external.CertificateThumbprint);
            }
            if (external.Macaroon != null)
            {
                model.Macaroon = Encoders.Hex.EncodeData(external.Macaroon);
            }

            if (nonce != null)
            {
                var configKey = GetConfigKey("lnd-grpc", cryptoCode, index, nonce.Value);
                var lnConfig = _LnConfigProvider.GetConfig(configKey);
                if (lnConfig != null)
                {
                    model.QRCodeLink = $"{this.Request.GetAbsoluteRoot().WithTrailingSlash()}lnd-config/{configKey}/lnd.config";
                    model.QRCode = $"config={model.QRCodeLink}";
                }
            }

            return View(model);
        }

        private static uint GetConfigKey(string type, string cryptoCode, int index, uint nonce)
        {
            return (uint)HashCode.Combine(type, cryptoCode, index, nonce);
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

        [Route("server/services/lnd-grpc/{cryptoCode}/{index}")]
        [HttpPost]
        public IActionResult LndGrpcServicesPost(string cryptoCode, int index)
        {
            var external = GetExternalLndConnectionString(cryptoCode, index);
            if (external == null)
                return NotFound();
            LightningConfigurations confs = new LightningConfigurations();
            LightningConfiguration conf = new LightningConfiguration();
            conf.Type = "grpc";
            conf.ChainType = _Options.NetworkType.ToString();
            conf.CryptoCode = cryptoCode;
            conf.Host = external.BaseUri.DnsSafeHost;
            conf.Port = external.BaseUri.Port;
            conf.SSL = external.BaseUri.Scheme == "https";
            conf.Macaroon = external.Macaroon == null ? null : Encoders.Hex.EncodeData(external.Macaroon);
            conf.CertificateThumbprint = external.CertificateThumbprint == null ? null : Encoders.Hex.EncodeData(external.CertificateThumbprint);
            confs.Configurations.Add(conf);

            var nonce = RandomUtils.GetUInt32();
            var configKey = GetConfigKey("lnd-grpc", cryptoCode, index, nonce);
            _LnConfigProvider.KeepConfig(configKey, confs);
            return RedirectToAction(nameof(LndGrpcServices), new { cryptoCode = cryptoCode, nonce = nonce });
        }

        private LightningConnectionString GetExternalLndConnectionString(string cryptoCode, int index)
        {
            var connectionString = _Options.ExternalServicesByCryptoCode.GetServices<ExternalLnd>(cryptoCode).Skip(index).Select(c => c.ConnectionString).FirstOrDefault();
            if (connectionString == null)
                return null;
            connectionString = connectionString.Clone();
            if (connectionString.MacaroonFilePath != null)
            {
                try
                {
                    connectionString.Macaroon = System.IO.File.ReadAllBytes(connectionString.MacaroonFilePath);
                    connectionString.MacaroonFilePath = null;
                }
                catch
                {
                    Logs.Configuration.LogWarning($"{cryptoCode}: The macaroon file path of the external LND grpc config was not found ({connectionString.MacaroonFilePath})");
                    return null;
                }
            }
            return connectionString;
        }

        [Route("server/services/lnd-rest/{cryptoCode}/{index}")]
        public IActionResult LndRestServices(string cryptoCode, int index, uint? nonce)
        {
            if (!_dashBoard.IsFullySynched(cryptoCode, out var unusud))
            {
                StatusMessage = $"Error: {cryptoCode} is not fully synched";
                return RedirectToAction(nameof(Services));
            }
            var external = GetExternalLndConnectionString(cryptoCode, index);
            if (external == null)
                return NotFound();
            var model = new LndRestServicesViewModel();

            model.BaseApiUrl = external.BaseUri.ToString();
            if (external.CertificateThumbprint != null)
                model.CertificateThumbprint = Encoders.Hex.EncodeData(external.CertificateThumbprint);
            if (external.Macaroon != null)
                model.Macaroon = Encoders.Hex.EncodeData(external.Macaroon);

            return View(model);
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
            SSHServiceViewModel vm = new SSHServiceViewModel();
            string port = settings.Port == 22 ? "" : $" -p {settings.Port}";
            vm.CommandLine = $"ssh {settings.Username}@{settings.Server}{port}";
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
        [HttpPost]
        public async Task<IActionResult> Emails(EmailsViewModel model, string command)
        {
            if (command == "Test")
            {
                try
                {
                    if (!model.Settings.IsComplete())
                    {
                        model.StatusMessage = "Error: Required fields missing";
                        return View(model);
                    }
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

                if (string.IsNullOrEmpty(file)) return View("Logs", vm);
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
