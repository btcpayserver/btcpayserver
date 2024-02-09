#nullable enable
using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client.Models;
using BTCPayServer.Controllers;
using BTCPayServer.Data;
using BTCPayServer.HostedServices;
using BTCPayServer.NTag424;
using BTCPayServer.Services;
using BTCPayServer.Services.Apps;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NBitcoin.DataEncoders;

namespace BTCPayServer.Plugins.BoltcardFactory.Controllers
{
    [ApiController]
    [Route("apps")]
    public class APIBoltcardFactoryController : ControllerBase
    {
        private readonly ILogger<APIBoltcardFactoryController> _logger;
        private readonly AppService _appService;
        private readonly SettingsRepository _settingsRepository;
        private readonly BTCPayServerEnvironment _env;
        private readonly ApplicationDbContextFactory _dbContextFactory;
        private readonly PullPaymentHostedService _ppService;

        public APIBoltcardFactoryController(
            ILogger<APIBoltcardFactoryController> logger,
            AppService appService,
            SettingsRepository settingsRepository,
            BTCPayServerEnvironment env,
            ApplicationDbContextFactory dbContextFactory,
            PullPaymentHostedService ppService)
        {
            _logger = logger;
            _appService = appService;
            _settingsRepository = settingsRepository;
            _env = env;
            _dbContextFactory = dbContextFactory;
            _ppService = ppService;
        }
        [HttpPost("{appId}/boltcards")]
        [AllowAnonymous]
        public async Task<IActionResult> RegisterBoltcard(string appId, RegisterBoltcardRequest? request, string? onExisting = null)
        {
            var app = await _appService.GetApp(appId, BoltcardFactoryPlugin.AppType);
            if (app is null)
                return NotFound();
            var issuerKey = await _settingsRepository.GetIssuerKey(_env);

            // LNURLW is used by deeplinks
            if (request?.LNURLW is not null)
            {
                if (request.UID is not null)
                {
                    _logger.LogInformation("You should pass either LNURLW or UID but not both");
                    ModelState.AddModelError(nameof(request.LNURLW), "You should pass either LNURLW or UID but not both");
                    return this.CreateValidationError(ModelState);
                }
                var p = ExtractP(request.LNURLW);
                if (p is null)
                {
                    _logger.LogInformation("The LNURLW should contains a 'p=' parameter");
                    ModelState.AddModelError(nameof(request.LNURLW), "The LNURLW should contains a 'p=' parameter");
                    return this.CreateValidationError(ModelState);
                }
                if (issuerKey.TryDecrypt(p) is not BoltcardPICCData picc)
                {
                    _logger.LogInformation("The LNURLW 'p=' parameter cannot be decrypted");
                    ModelState.AddModelError(nameof(request.LNURLW), "The LNURLW 'p=' parameter cannot be decrypted");
                    return this.CreateValidationError(ModelState);
                }
                request.UID = picc.Uid;
            }

            if (request?.UID is null || request.UID.Length != 7)
            {
                _logger.LogInformation("The UID is required and should be 7 bytes");
                ModelState.AddModelError(nameof(request.UID), "The UID is required and should be 7 bytes");
                return this.CreateValidationError(ModelState);
            }

            // Passing onExisting as a query parameter is used by deeplink
            request.OnExisting = onExisting switch
            {
                nameof(OnExistingBehavior.UpdateVersion) => OnExistingBehavior.UpdateVersion,
                nameof(OnExistingBehavior.KeepVersion) => OnExistingBehavior.KeepVersion,
                _ => request.OnExisting
            };

            int version;
            string ppId;
            var registration = await _dbContextFactory.GetBoltcardRegistration(issuerKey, request.UID);

            if (request.OnExisting == OnExistingBehavior.UpdateVersion)
            {
                var req = app.GetSettings<CreatePullPaymentRequest>();
                ppId = await _ppService.CreatePullPayment(app.StoreDataId, req);
                version = await _dbContextFactory.LinkBoltcardToPullPayment(ppId, issuerKey, request.UID, request.OnExisting);
            }
            // If it's a reset, do not create a new pull payment
            else
            {
                if (registration?.PullPaymentId is null)
                {
                    _logger.LogInformation("This card isn't registered");
                    ModelState.AddModelError(nameof(request.UID), "This card isn't registered");
                    return this.CreateValidationError(ModelState);
                }
                ppId = registration.PullPaymentId;
                version = registration.Version;
            }

            var keys = issuerKey.CreatePullPaymentCardKey(request.UID, version, ppId).DeriveBoltcardKeys(issuerKey);
            var boltcardUrl = Url.Action(nameof(UIBoltcardController.GetWithdrawRequest), "UIBoltcard");
            boltcardUrl = Request.GetAbsoluteUri(boltcardUrl);
            boltcardUrl = Regex.Replace(boltcardUrl, "^https?://", "lnurlw://");

            var resp = new RegisterBoltcardResponse()
            {
                LNURLW = boltcardUrl,
                Version = version,
                K0 = Encoders.Hex.EncodeData(keys.AppMasterKey.ToBytes()).ToUpperInvariant(),
                K1 = Encoders.Hex.EncodeData(keys.EncryptionKey.ToBytes()).ToUpperInvariant(),
                K2 = Encoders.Hex.EncodeData(keys.AuthenticationKey.ToBytes()).ToUpperInvariant(),
                K3 = Encoders.Hex.EncodeData(keys.K3.ToBytes()).ToUpperInvariant(),
                K4 = Encoders.Hex.EncodeData(keys.K4.ToBytes()).ToUpperInvariant(),
            };

            return Ok(resp);
        }
        private string? ExtractP(string? url)
        {
            if (url is null || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return null;
            int num = uri.AbsoluteUri.IndexOf('?');
            if (num == -1)
                return null;
            string input = uri.AbsoluteUri.Substring(num);
            Match match = Regex.Match(input, "p=([a-f0-9A-F]{32})");
            if (!match.Success)
                return null;
            return match.Groups[1].Value;
        }
    }
}
