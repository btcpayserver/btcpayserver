using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Data;
using BTCPayServer.Filters;
using BTCPayServer.HostedServices;
using BTCPayServer.Models;
using BTCPayServer.Models.StoreViewModels;
using BTCPayServer.Security;
using BTCPayServer.Services;
using BTCPayServer.Services.Apps;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;
using NBitcoin;
using NBitcoin.Payment;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Controllers
{
    public class HomeController : Controller
    {
        private readonly CssThemeManager _cachedServerSettings;
        private readonly IFileProvider _fileProvider;

        public IHttpClientFactory HttpClientFactory { get; }
        public LanguageService LanguageService { get; }
        SignInManager<ApplicationUser> SignInManager { get; }

        public HomeController(IHttpClientFactory httpClientFactory,
                              CssThemeManager cachedServerSettings,
                              IWebHostEnvironment webHostEnvironment,
                              LanguageService languageService,
                              SignInManager<ApplicationUser> signInManager)
        {
            HttpClientFactory = httpClientFactory;
            _cachedServerSettings = cachedServerSettings;
            LanguageService = languageService;
            _fileProvider = webHostEnvironment.WebRootFileProvider;
            SignInManager = signInManager;
        }

        [Route("")]
        [DomainMappingConstraint()]
        public IActionResult Index()
        {
            if (_cachedServerSettings.FirstRun)
            {
                return RedirectToAction(nameof(AccountController.Register), "Account");
            }
            if (SignInManager.IsSignedIn(User))
                return View("Home");
            else
                return Challenge();
        }

        [Route("misc/lang")]
        [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
        public IActionResult Languages()
        {
            return Json(LanguageService.GetLanguages(), new JsonSerializerSettings() { Formatting = Formatting.Indented });
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
        [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
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
