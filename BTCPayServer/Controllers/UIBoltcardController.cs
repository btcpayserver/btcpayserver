#nullable enable
using Dapper;
using System.Linq;
using System.Security;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.NTag424;
using BTCPayServer.Services;
using LNURL;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Threading;
using System;
using NBitcoin.DataEncoders;
using System.Text.Json.Serialization;

namespace BTCPayServer.Controllers;

public class UIBoltcardController : Controller
{
    public class BoltcardSettings
    {
        [JsonConverter(typeof(NBitcoin.JsonConverters.HexJsonConverter))]
        public byte[]? IssuerKey { get; set; }
    }
    public UIBoltcardController(
        UILNURLController lnUrlController,
        SettingsRepository settingsRepository,
        ApplicationDbContextFactory contextFactory,
        BTCPayServerEnvironment env)
    {
        LNURLController = lnUrlController;
        SettingsRepository = settingsRepository;
        ContextFactory = contextFactory;
        Env = env;
    }

    public UILNURLController LNURLController { get; }
    public SettingsRepository SettingsRepository { get; }
    public ApplicationDbContextFactory ContextFactory { get; }
    public BTCPayServerEnvironment Env { get; }

    [AllowAnonymous]
    [HttpGet("~/boltcard")]
    public async Task<IActionResult> GetWithdrawRequest([FromQuery] string? p, [FromQuery] string? c, [FromQuery] string? pr, [FromQuery] string? k1, CancellationToken cancellationToken)
    {
        if (p is null || c is null)
        {
            var k1s = k1?.Split('-');
            if (k1s is not { Length: 2 })
                return BadRequest(new LNUrlStatusResponse { Status = "ERROR", Reason = "Missing p, c, or k1 query parameter" });
            p = k1s[0];
            c = k1s[1];
        }
        var issuerKey = await SettingsRepository.GetIssuerKey(Env);
        var piccData = issuerKey.TryDecrypt(p);
        if (piccData is null)
            return BadRequest(new LNUrlStatusResponse { Status = "ERROR", Reason = "Invalid PICCData" });

        var registration = await ContextFactory.GetBoltcardRegistration(issuerKey, piccData, updateCounter: pr is not null);
        if (registration?.PullPaymentId is null)
            return BadRequest(new LNUrlStatusResponse { Status = "ERROR", Reason = "Replayed or expired query" });
        var cardKey = issuerKey.CreatePullPaymentCardKey(piccData.Uid, registration.Version, registration.PullPaymentId);
        if (!cardKey.CheckSunMac(c, piccData))
            return BadRequest(new LNUrlStatusResponse { Status = "ERROR", Reason = "Replayed or expired query" });
        LNURLController.ControllerContext.HttpContext = HttpContext;
        return await LNURLController.GetLNURLForPullPayment("BTC", registration.PullPaymentId, pr, $"{p}-{c}", true, cancellationToken);
    }
}
