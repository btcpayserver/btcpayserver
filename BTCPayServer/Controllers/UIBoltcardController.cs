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
using BTCPayServer.HostedServices;
using BTCPayServer.Services.Stores;
using System.Collections.Generic;
using BTCPayServer.Client.Models;
using BTCPayServer.Lightning;
using System.Reflection.Metadata;

namespace BTCPayServer.Controllers;

public class UIBoltcardController : Controller
{
    private readonly PullPaymentHostedService _ppService;
    private readonly StoreRepository _storeRepository;

    public class BoltcardSettings
    {
        [JsonConverter(typeof(NBitcoin.JsonConverters.HexJsonConverter))]
        public byte[]? IssuerKey { get; set; }
    }
    public UIBoltcardController(
        UILNURLController lnUrlController,
        SettingsRepository settingsRepository,
        ApplicationDbContextFactory contextFactory,
        PullPaymentHostedService ppService,
        StoreRepository storeRepository,
        BTCPayServerEnvironment env)
    {
        LNURLController = lnUrlController;
        SettingsRepository = settingsRepository;
        ContextFactory = contextFactory;
        _ppService = ppService;
        _storeRepository = storeRepository;
        Env = env;
    }

    public UILNURLController LNURLController { get; }
    public SettingsRepository SettingsRepository { get; }
    public ApplicationDbContextFactory ContextFactory { get; }
    public BTCPayServerEnvironment Env { get; }

    [AllowAnonymous]
    [HttpGet("~/boltcard/pay")]
    public async Task<IActionResult> GetPayRequest([FromQuery] string? p, [FromQuery] long? amount = null)
    {
        var issuerKey = await SettingsRepository.GetIssuerKey(Env);
        var piccData = issuerKey.TryDecrypt(p);
        if (piccData is null)
            return BadRequest(new LNUrlStatusResponse { Status = "ERROR", Reason = "Invalid PICCData" });

        piccData = new BoltcardPICCData(piccData.Uid, int.MaxValue - 10); // do not check the counter
        var registration = await ContextFactory.GetBoltcardRegistration(issuerKey, piccData, false);
        var pp = await _ppService.GetPullPayment(registration!.PullPaymentId, false);
        var store = await _storeRepository.FindStore(pp.StoreId);

        var lnUrlMetadata = new Dictionary<string, string>();
        lnUrlMetadata.Add("text/plain", "Boltcard Top-Up");
        var payRequest = new LNURLPayRequest
        {
            Tag = "payRequest",
            MinSendable = LightMoney.Satoshis(1.0m),
            MaxSendable = LightMoney.FromUnit(6.12m, LightMoneyUnit.BTC),
            Callback = new Uri(GetPayLink(p, Request.Scheme), UriKind.Absolute),
            CommentAllowed = 0
        };
        payRequest.Metadata = Newtonsoft.Json.JsonConvert.SerializeObject(lnUrlMetadata.Select(kv => new[] { kv.Key, kv.Value }));
        if (amount is null)
            return Ok(payRequest);

        var cryptoCode = "BTC";

        var currency = "BTC";
        var invoiceAmount = LightMoney.FromUnit(amount.Value, LightMoneyUnit.MilliSatoshi).ToUnit(LightMoneyUnit.BTC);

        if (pp.GetBlob().Currency == "SATS")
        {
            currency = "SATS";
            invoiceAmount = LightMoney.FromUnit(amount.Value, LightMoneyUnit.MilliSatoshi).ToUnit(LightMoneyUnit.Satoshi);
        }

        LNURLController.ControllerContext.HttpContext = HttpContext;
        var result = await LNURLController.GetLNURLRequest(
               cryptoCode,
               store,
               store.GetStoreBlob(),
               new CreateInvoiceRequest()
               {
                   Currency = currency,
                   Amount = invoiceAmount
               },
               payRequest,
               lnUrlMetadata,
               [PullPaymentHostedService.GetInternalTag(pp.Id)]);
        if (result is not OkObjectResult ok || ok.Value is not LNURLPayRequest payRequest2)
            return result;
        payRequest = payRequest2;
        var invoiceId = payRequest.Callback.AbsoluteUri.Split('/').Last();
        return await LNURLController.GetLNURLForInvoice(invoiceId, cryptoCode, amount.Value, null);
    }
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
        var res = await LNURLController.GetLNURLForPullPayment("BTC", registration.PullPaymentId, pr, $"{p}-{c}", cancellationToken);
        if (res is not OkObjectResult ok || ok.Value is not LNURLWithdrawRequest withdrawRequest)
            return res;
        var paylink = GetPayLink(p, "lnurlp");
        withdrawRequest.PayLink = new Uri(paylink, UriKind.Absolute);
        return res;
    }

    private string GetPayLink(string? p, string scheme)
    {
        return Url.Action(nameof(GetPayRequest), "UIBoltcard", new { p }, scheme)!;
    }
}
