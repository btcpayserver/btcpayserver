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
using BTCPayServer.Data;
using BTCPayServer.Filters;
using BTCPayServer.Models;
using BTCPayServer.Models.StoreViewModels;
using BTCPayServer.Services;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Controllers
{
    public class UIHomeController : Controller
    {
        private readonly ThemeSettings _theme;
        private readonly StoreRepository _storeRepository;
        private IHttpClientFactory HttpClientFactory { get; }
        private SignInManager<ApplicationUser> SignInManager { get; }

        private IFileProvider _WebRootFileProvider;

        public LanguageService LanguageService { get; }

        public UIHomeController(IHttpClientFactory httpClientFactory,
                              ThemeSettings theme,
                              LanguageService languageService,
                              StoreRepository storeRepository,
                              IWebHostEnvironment environment,
                              SignInManager<ApplicationUser> signInManager)
        {
            _theme = theme;
            HttpClientFactory = httpClientFactory;
            LanguageService = languageService;
            _storeRepository = storeRepository;
            SignInManager = signInManager;
            _WebRootFileProvider = environment.WebRootFileProvider;
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
            if (_theme.FirstRun)
            {
                return RedirectToAction(nameof(UIAccountController.Register), "UIAccount");
            }

            if (SignInManager.IsSignedIn(User))
            {
                var userId = SignInManager.UserManager.GetUserId(HttpContext.User);
                var storeId = HttpContext.GetUserPrefsCookie()?.CurrentStoreId;
                if (storeId != null && userId != null)
                {
                    // verify store exists and redirect to it
                    var store = await _storeRepository.FindStore(storeId, userId);
                    if (store != null)
                    {
                        return RedirectToAction(nameof(UIStoresController.Index), "UIStores", new { storeId });
                    }
                    HttpContext.DeleteUserPrefsCookie();
                }

                var stores = await _storeRepository.GetStoresByUserId(userId!);
                var activeStore = stores.FirstOrDefault(s => !s.Archived);
                return activeStore != null
                    ? RedirectToAction(nameof(UIStoresController.Index), "UIStores", new { storeId = activeStore.Id })
                    : RedirectToAction(nameof(UIUserStoresController.CreateStore), "UIUserStores");
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
        [Route("misc/translations/{resource}/{lang}")]
        [AllowAnonymous]
        public IActionResult GetTranslations(string resource, string lang)
        {
            string path;
            if (resource.StartsWith("checkout"))
                path = "locales/checkout";
            else
                return NotFound();
            var enLang = Lang(path + "/en.json");
            var en = (enLang as JsonResult)?.Value as JObject;
            if (en is null || lang == "en" || lang == "en-US")
                return enLang;
            lang = LanguageService.FindLanguage(lang)?.Code;
            if (lang is null)
                return enLang;
            var oLang = Lang(path + $"/{lang}.json");
            var o = (oLang as JsonResult)?.Value as JObject;
            if (o is null)
                return enLang;
            en.Merge(o);
            return Json(en);
        }

        private IActionResult Lang(string path)
        {
            var fi = _WebRootFileProvider.GetFileInfo(path);
            try
            {
                using var fs = fi.CreateReadStream();
                return Json(JObject.Load(new JsonTextReader(new StreamReader(fs, leaveOpen: true))));
            }
            catch
            {
                return NotFound();
            }
        }

        [Route("swagger/v1/swagger.json")]
        [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie + "," + AuthenticationSchemes.Greenfield)]
        public async Task<IActionResult> Swagger([FromServices] IEnumerable<ISwaggerProvider> swaggerProviders)
        {
            JObject json = new();
            var res = await Task.WhenAll(swaggerProviders.Select(provider => provider.Fetch()));
            foreach (JObject jObject in res)
            {
                json.Merge(jObject);
            }
            var servers = new JArray();
            servers.Add(new JObject(new JProperty("url", HttpContext.Request.GetAbsoluteRoot())));
            json["servers"] = servers;
            var tags = (JArray)json["tags"];
            json["tags"] = new JArray(tags
                .Select(o => (name: ((JObject)o)["name"].Value<string>(), o))
                .OrderBy(o => o.name)
                .Select(o => o.o)
                .ToArray());
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
    }
}
