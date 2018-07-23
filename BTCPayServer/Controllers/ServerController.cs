using BTCPayServer.Configuration;
using BTCPayServer.HostedServices;
using BTCPayServer.Models;
using BTCPayServer.Models.ServerViewModels;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.Services;
using BTCPayServer.Services.Mails;
using BTCPayServer.Services.Rates;
using BTCPayServer.Services.Stores;
using BTCPayServer.Validations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;
using NBitcoin.DataEncoders;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Mail;
using System.Threading.Tasks;

namespace BTCPayServer.Controllers
{
    [Authorize(Policy = BTCPayServer.Security.Policies.CanModifyServerSettings.Key)]
    public class ServerController : Controller
    {
        private UserManager<ApplicationUser> _UserManager;
        SettingsRepository _SettingsRepository;
        private BTCPayRateProviderFactory _RateProviderFactory;
        private StoreRepository _StoreRepository;
        LightningConfigurationProvider _LnConfigProvider;
        BTCPayServerOptions _Options;

        public ServerController(UserManager<ApplicationUser> userManager,
            Configuration.BTCPayServerOptions options,
            BTCPayRateProviderFactory rateProviderFactory,
            SettingsRepository settingsRepository,
            LightningConfigurationProvider lnConfigProvider,
            Services.Stores.StoreRepository storeRepository)
        {
            _Options = options;
            _UserManager = userManager;
            _SettingsRepository = settingsRepository;
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
                {
                    int i = 0;
                    foreach (var grpcService in _Options.ExternalServicesByCryptoCode.GetServices<ExternalLNDGRPC>(cryptoCode))
                    {
                        result.LNDServices.Add(new ServicesViewModel.LNDServiceViewModel()
                        {
                            Crypto = cryptoCode,
                            Type = "gRPC",
                            Index = i++,
                        });
                    }
                }
            }
            return View(result);
        }

        [Route("server/services/lnd-grpc/{cryptoCode}/{index}")]
        public IActionResult LNDGRPCServices(string cryptoCode, int index, uint? nonce)
        {
            var external = GetExternalLNDConnectionString(cryptoCode, index);
            if (external == null)
                return NotFound();
            var model = new LNDGRPCServicesViewModel();

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
        public IActionResult LNDGRPCServicesPOST(string cryptoCode, int index)
        {
            var external = GetExternalLNDConnectionString(cryptoCode, index);
            if (external == null)
                return NotFound();
            LightningConfigurations confs = new LightningConfigurations();
            LightningConfiguration conf = new LightningConfiguration();
            conf.Type = "grpc";
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
            return RedirectToAction(nameof(LNDGRPCServices), new { cryptoCode = cryptoCode, nonce = nonce });
        }

        private LightningConnectionString GetExternalLNDConnectionString(string cryptoCode, int index)
        {
            return _Options.ExternalServicesByCryptoCode.GetServices<ExternalLNDGRPC>(cryptoCode).Skip(index).Select(c => c.ConnectionString).FirstOrDefault();
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
    }
}
