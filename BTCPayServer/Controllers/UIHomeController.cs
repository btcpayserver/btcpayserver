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
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly IFileProvider _webRootFileProvider;
        public LanguageService LanguageService { get; }

        public UIHomeController(
            IHttpClientFactory httpClientFactory,
            ThemeSettings theme,
            LanguageService languageService,
            StoreRepository storeRepository,
            IWebHostEnvironment environment,
            SignInManager<ApplicationUser> signInManager)
        {
            _theme = theme;
            _httpClientFactory = httpClientFactory;
            LanguageService = languageService;
            _storeRepository = storeRepository;
            _signInManager = signInManager;
            _webRootFileProvider = environment.WebRootFileProvider;
        }

        [HttpGet("home")]
        public Task<IActionResult> Home() => Index();

        [Route("")]
        [DomainMappingConstraint]
        public async Task<IActionResult> Index()
        {
            if (_theme.FirstRun)
                return RedirectToAction(nameof(UIAccountController.Register), "UIAccount");

            if (_signInManager.IsSignedIn(User))
            {
                var userId = _signInManager.UserManager.GetUserId(User);
                var storeId = HttpContext.GetUserPrefsCookie()?.CurrentStoreId;

                if (storeId != null)
                {
                    var store = await _storeRepository.FindStore(storeId);
                    if (store != null)
                        return RedirectToAction(nameof(UIStoresController.Index), "UIStores", new { storeId });
                }

                var stores = await _storeRepository.GetStoresByUserId(userId!);
                var activeStore = stores.FirstOrDefault(s => !s.Archived);

                return activeStore != null
                    ? RedirectToAction(nameof(UIStoresController.Index), "UIStores", new { storeId = activeStore.Id })
                    : RedirectToAction(nameof(UIUserStoresController.CreateStore), "UIUserStores");
            }

            return Challenge();
        }

        // ----- JSON Endpoints -----

        [HttpGet("misc/lang")]
        [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie + "," + AuthenticationSchemes.Greenfield)]
        public IActionResult Languages() =>
            Json(LanguageService.GetLanguages(), new JsonSerializerSettings { Formatting = Formatting.Indented });

        [HttpGet("misc/permissions")]
        [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie + "," + AuthenticationSchemes.Greenfield)]
        public IActionResult Permissions() =>
            Json(Client.Models.PermissionMetadata.PermissionNodes, new JsonSerializerSettings { Formatting = Formatting.Indented });

        [HttpGet("misc/translations/{resource}/{lang}")]
        [AllowAnonymous]
        public IActionResult GetTranslations(string resource, string lang)
        {
            if (!resource.StartsWith("checkout"))
                return NotFound();

            var basePath = "locales/checkout";
            var englishJsonResult = LoadJsonFromFile($"{basePath}/en.json");
            var englishJson = (englishJsonResult as JsonResult)?.Value as JObject;

            if (englishJson == null || lang == "en" || lang == "en-US")
                return englishJsonResult;

            var matchedLang = LanguageService.FindLanguage(lang)?.Code;
            if (matchedLang == null)
                return englishJsonResult;

            var localizedJsonResult = LoadJsonFromFile($"{basePath}/{matchedLang}.json");
            var localizedJson = (localizedJsonResult as JsonResult)?.Value as JObject;

            if (localizedJson == null)
                return englishJsonResult;

            englishJson.Merge(localizedJson);
            return Json(englishJson);
        }

        private IActionResult LoadJsonFromFile(string path)
        {
            var file = _webRootFileProvider.GetFileInfo(path);
            try
            {
                using var stream = file.CreateReadStream();
                using var reader = new StreamReader(stream);
                using var jsonReader = new JsonTextReader(reader);
                return Json(JObject.Load(jsonReader));
            }
            catch
            {
                return NotFound();
            }
        }

        // ----- Swagger -----

        [HttpGet("swagger/v1/swagger.json")]
        [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie + "," + AuthenticationSchemes.Greenfield)]
        public async Task<IActionResult> Swagger([FromServices] IEnumerable<ISwaggerProvider> swaggerProviders)
        {
            var mergedJson = new JObject();
            var swaggerDocs = await Task.WhenAll(swaggerProviders.Select(p => p.Fetch()));
            foreach (var doc in swaggerDocs)
                mergedJson.Merge(doc);

            // Add server info
            mergedJson["servers"] = new JArray(new JObject(new JProperty("url", HttpContext.Request.GetAbsoluteRoot())));

            // Sort tags alphabetically
            if (mergedJson["tags"] is JArray tags)
            {
                mergedJson["tags"] = new JArray(tags
                    .Select(t => (name: t["name"]?.ToString(), tag: t))
                    .OrderBy(t => t.name)
                    .Select(t => t.tag));
            }

            return Json(mergedJson);
        }

        [HttpGet("docs")]
        [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        public IActionResult SwaggerDocs() => View();

        // ----- Recovery Seed -----

        [HttpGet("recovery-seed-backup")]
        [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie, Policy = Policies.CanModifyStoreSettings)]
        public IActionResult RecoverySeedBackup(RecoverySeedBackupViewModel vm) =>
            View("RecoverySeedBackup", vm);

        // ----- Utilities -----

        [HttpPost("postredirect-callback-test")]
        public IActionResult PostRedirectCallbackTestpage(IFormCollection formData)
        {
            var result = formData.Keys.ToDictionary(k => k, k => formData[k]);
            return Json(result);
        }

        [HttpGet("error")]
        public IActionResult Error() =>
            View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
