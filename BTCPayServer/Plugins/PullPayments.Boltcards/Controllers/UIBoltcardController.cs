#nullable enable
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Services;
using LNURL;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Threading;
using System.Text.Json.Serialization;

// Don't change the namespace, boltcard factory depends on it
namespace BTCPayServer.Controllers;

// No area, boltcard factory depends on it
public class UIBoltcardController(
    UILNURLController lnUrlController,
    SettingsRepository settingsRepository,
    ApplicationDbContextFactory contextFactory,
    BTCPayServerEnvironment env)
    : Controller
{
    public class BoltcardSettings
    {
        [JsonConverter(typeof(NBitcoin.JsonConverters.HexJsonConverter))]
        public byte[]? IssuerKey { get; set; }
    }

    [AllowAnonymous]
    [HttpGet("~/boltcard")]
    public async Task<IActionResult> GetWithdrawRequest([FromQuery] string? p, [FromQuery] string? c, [FromQuery] string? pr, [FromQuery] string? k1, CancellationToken cancellationToken)
    {
        if (p is null || c is null)
        {
            // ReSharper disable once InconsistentNaming
            var k1s = k1?.Split('-');
            if (k1s is not { Length: 2 })
                return BadRequest(new LNUrlStatusResponse { Status = "ERROR", Reason = "Missing p, c, or k1 query parameter" });
            p = k1s[0];
            c = k1s[1];
        }
        var issuerKey = await settingsRepository.GetIssuerKey(env);
        var piccData = issuerKey.TryDecrypt(p);
        if (piccData is null)
            return BadRequest(new LNUrlStatusResponse { Status = "ERROR", Reason = "Invalid PICCData" });

        var registration = await contextFactory.GetBoltcardRegistration(issuerKey, piccData, updateCounter: pr is not null);
        if (registration?.PullPaymentId is null)
            return BadRequest(new LNUrlStatusResponse { Status = "ERROR", Reason = "Replayed or expired query" });
        var cardKey = issuerKey.CreatePullPaymentCardKey(piccData.Uid, registration.Version, registration.PullPaymentId);
        if (!cardKey.CheckSunMac(c, piccData))
            return BadRequest(new LNUrlStatusResponse { Status = "ERROR", Reason = "Replayed or expired query" });
        lnUrlController.ControllerContext.HttpContext = HttpContext;
        return await lnUrlController.GetLNURLForPullPayment("BTC", registration.PullPaymentId, pr, $"{p}-{c}", true, cancellationToken);
    }
}
