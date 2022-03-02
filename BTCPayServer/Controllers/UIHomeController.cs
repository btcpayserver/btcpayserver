using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client;
using BTCPayServer.Components.StoreSelector;
using BTCPayServer.Data;
using BTCPayServer.Filters;
using BTCPayServer.HostedServices;
using BTCPayServer.Models;
using BTCPayServer.Models.StoreViewModels;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.Security;
using BTCPayServer.Services;
using BTCPayServer.Services.Apps;
using BTCPayServer.Services.Stores;
using ExchangeSharp;
using Google.Apis.Auth.OAuth2;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;
using NBitcoin;
using NBitcoin.Payment;
using NBitpayClient;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Controllers
{
    public class UIHomeController : Controller
    {
        private readonly ISettingsRepository _settingsRepository;
        private readonly StoreRepository _storeRepository;
        private readonly IFileProvider _fileProvider;
        private readonly BTCPayNetworkProvider _networkProvider;
        private IHttpClientFactory HttpClientFactory { get; }
        private SignInManager<ApplicationUser> SignInManager { get; }
        public LanguageService LanguageService { get; }

        public UIHomeController(IHttpClientFactory httpClientFactory,
                              ISettingsRepository settingsRepository,
                              IWebHostEnvironment webHostEnvironment,
                              LanguageService languageService,
                              StoreRepository storeRepository,
                              BTCPayNetworkProvider networkProvider,
                              SignInManager<ApplicationUser> signInManager)
        {
            _settingsRepository = settingsRepository;
            HttpClientFactory = httpClientFactory;
            LanguageService = languageService;
            _networkProvider = networkProvider;
            _storeRepository = storeRepository;
            _fileProvider = webHostEnvironment.WebRootFileProvider;
            SignInManager = signInManager;
        }

        [HttpGet("home")]
        public Task<IActionResult> Home()
        {
            return Index();
        }

        [Route("")]
        [DomainMappingConstraint]
        public async Task<IActionResult> Index()
        {
            if ((await _settingsRepository.GetTheme()).FirstRun)
            {
                return RedirectToAction(nameof(UIAccountController.Register), "UIAccount");
            }

            if (SignInManager.IsSignedIn(User))
            {
                var userId = SignInManager.UserManager.GetUserId(HttpContext.User);
                var storeId = HttpContext.GetUserPrefsCookie()?.CurrentStoreId;
                if (storeId != null)
                {
                    // verify store exists and redirect to it
                    var store = await _storeRepository.FindStore(storeId, userId);
                    if (store != null)
                    {
                        return RedirectToStore(store);
                    }
                }
                
                var stores = await _storeRepository.GetStoresByUserId(userId);
                if (stores.Any())
                {
                    // redirect to first store
                    return RedirectToStore(stores.First());
                }
                
                var vm = new HomeViewModel
                {
                    HasStore = stores.Any()
                };
                
                return View("Home", vm);
            }

            return Challenge();
        }

        [Route("misc/lang")]
        [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie + "," + AuthenticationSchemes.Greenfield)]
        public IActionResult Languages()
        {
            return Json(LanguageService.GetLanguages(), new JsonSerializerSettings { Formatting = Formatting.Indented });
        }

        [Route("misc/permissions")]
        [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie + "," + AuthenticationSchemes.Greenfield)]
        public IActionResult Permissions()
        {
            return Json(Client.Models.PermissionMetadata.PermissionNodes, new JsonSerializerSettings { Formatting = Formatting.Indented });
        }

        [Route("swagger/v1/swagger.json")]
        [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie + "," + AuthenticationSchemes.Greenfield)]
        public async Task<IActionResult> Swagger()
        {
            JObject json = new JObject();
            var directoryContents = _fileProvider.GetDirectoryContents("swagger/v1");
            foreach (IFileInfo fi in directoryContents)
            {
                await using var stream = fi.CreateReadStream();
                using var reader = new StreamReader(fi.CreateReadStream());
                json.Merge(JObject.Parse(await reader.ReadToEndAsync()));
            }
            var servers = new JArray();
            servers.Add(new JObject(new JProperty("url", HttpContext.Request.GetAbsoluteRoot())));
            json["servers"] = servers;
            return Json(json);
        }

        [Route("docs")]
        [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        public IActionResult SwaggerDocs()
        {
            return View();
        }

        [Route("recovery-seed-backup")]
        [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie, Policy = Policies.CanModifyStoreSettings)]
        public IActionResult RecoverySeedBackup(RecoverySeedBackupViewModel vm)
        {
            return View("RecoverySeedBackup", vm);
        }

        [HttpPost]
        [Route("postredirect-callback-test")]
        public ActionResult PostRedirectCallbackTestpage(IFormCollection data)
        {
            var list = data.Keys.Aggregate(new Dictionary<string, string>(), (res, key) =>
            {
                res.Add(key, data[key]);
                return res;
            });
            return Json(list);
        }

        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }

        public RedirectToActionResult RedirectToStore(StoreData store)
        {
            return store.Role == StoreRoles.Owner 
                ? RedirectToAction("Dashboard", "UIStores", new { storeId = store.Id }) 
                : RedirectToAction("ListInvoices", "UIInvoice", new { storeId = store.Id });
        }
    }
}
