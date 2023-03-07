using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client;
using BTCPayServer.Configuration;
using BTCPayServer.Data;
using BTCPayServer.Lightning;
using BTCPayServer.Lightning.LNDhub.Models;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.Security.Greenfield;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using NBitcoin;
using CreateInvoiceRequest = BTCPayServer.Lightning.LNDhub.Models.CreateInvoiceRequest;
using InvoiceData = BTCPayServer.Lightning.LNDhub.Models.InvoiceData;
using PaymentData = BTCPayServer.Lightning.LNDhub.Models.PaymentData;

namespace BTCPayServer.Controllers.Greenfield;

[ApiController]
[Authorize(AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
[LightningUnavailableExceptionFilter]
[EnableCors(CorsPolicies.All)]
[Route("~/api/v1/stores/{storeId}/lndhub/{cryptoCode}")]
public class GreenfieldStoreLndHubApiController : GreenfieldLightningNodeApiController
{
    private readonly IOptions<LightningNetworkOptions> _lightningNetworkOptions;
    private readonly LightningClientFactoryService _lightningClientFactory;
    private readonly BTCPayNetworkProvider _btcPayNetworkProvider;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly APIKeyRepository _apiKeyRepository;

    public GreenfieldStoreLndHubApiController(
        IOptions<LightningNetworkOptions> lightningNetworkOptions,
        LightningClientFactoryService lightningClientFactory, BTCPayNetworkProvider btcPayNetworkProvider,
        PoliciesSettings policiesSettings,
        APIKeyRepository apiKeyRepository,
        UserManager<ApplicationUser> userManager,
        IAuthorizationService authorizationService) : base(
        btcPayNetworkProvider, policiesSettings, authorizationService)
    {
        _lightningNetworkOptions = lightningNetworkOptions;
        _lightningClientFactory = lightningClientFactory;
        _btcPayNetworkProvider = btcPayNetworkProvider;
        _apiKeyRepository = apiKeyRepository;
        _userManager = userManager;
    }
    
    // https://github.com/BlueWallet/LndHub/blob/master/doc/Send-requirements.md#post-authtypeauth
    [AllowAnonymous]
    [HttpPost("auth")]
    public async Task<IActionResult> Auth(AuthRequest req, [FromQuery] string type)
    {
        AuthResponse result = null;

        switch (type)
        {
            case "auth":
                // login = user id, password = api key
                var apiKey = await _apiKeyRepository.GetKey(req.Password, true);
                if (apiKey != null && apiKey.UserId == req.Login && !await _userManager.IsLockedOutAsync(apiKey.User))
                {
                    result = new AuthResponse { AccessToken = apiKey.Id, RefreshToken = apiKey.Id };
                }
                break;

            // fake this case as we don't do OAuth
            case "refresh_token":
                result = new AuthResponse { AccessToken = req.RefreshToken, RefreshToken = req.RefreshToken };
                break;
        }

        return Ok(result != null ? result : new ErrorResponse(1));
    }

    // https://github.com/BlueWallet/LndHub/blob/master/doc/Send-requirements.md#get-getinfo
    [HttpGet("getinfo")]
    [Authorize(Policy = Policies.CanUseLightningNodeInStore, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
    public async Task<IActionResult> Info(string cryptoCode, CancellationToken cancellationToken = default)
    {
        var store = HttpContext.GetStoreData();
        var lightningClient = await GetLightningClient(cryptoCode, true);
        var info = await lightningClient.GetInfo(cancellationToken);
        var result = new NodeInfoData
        {
            Uris = info.NodeInfoList.Select(uri => uri.ToString()),
            IdentityPubkey = info.NodeInfoList.First().NodeId.ToString(),
            BlockHeight = info.BlockHeight,
            Alias = store.StoreName,
            Color = info.Color,
            Version = info.Version,
            PeersCount = info.PeersCount.HasValue ? Convert.ToInt32(info.PeersCount.Value) : 0,
            ActiveChannelsCount = info.ActiveChannelsCount.HasValue ? Convert.ToInt32(info.ActiveChannelsCount.Value) : 0,
            InactiveChannelsCount = info.InactiveChannelsCount.HasValue ? Convert.ToInt32(info.InactiveChannelsCount.Value) : 0,
            PendingChannelsCount = info.PendingChannelsCount.HasValue ? Convert.ToInt32(info.PendingChannelsCount.Value) : 0
        };
        return Ok(result);
    }

    // https://github.com/BlueWallet/LndHub/blob/master/doc/Send-requirements.md#get-getpending
    [HttpGet("getpending")]
    public IActionResult PendingTransactions(string cryptoCode, CancellationToken cancellationToken = default)
    {
        // There are no pending BTC transactions, so leave it as an empty implementation
        return Ok(new List<TransactionData>());
    }

    // https://github.com/BlueWallet/LndHub/blob/master/doc/Send-requirements.md#get-gettxs
    [HttpGet("gettxs")]
    [Authorize(Policy = Policies.CanUseLightningNodeInStore, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
    public async Task<IActionResult> Transactions(string cryptoCode, [FromQuery] int? limit, [FromQuery] int? offset, CancellationToken cancellationToken = default)
    {
        var network = _btcPayNetworkProvider.GetNetwork<BTCPayNetwork>(cryptoCode);
        if (network == null)
        {
            throw ErrorCryptoCodeNotFound();
        }
        
        var lightningClient = await GetLightningClient(cryptoCode, false);
        var param = new ListPaymentsParams { IncludePending = false, OffsetIndex = offset };
        var payments = await lightningClient.ListPayments(param, cancellationToken);
        var transactions = payments.Select(p => ToTransactionData(p, network));
        return Ok(transactions);
    }

    // https://github.com/BlueWallet/LndHub/blob/master/doc/Send-requirements.md#get-getuserinvoices
    [HttpGet("getuserinvoices")]
    [Authorize(Policy = Policies.CanViewLightningInvoiceInStore, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
    public async Task<IActionResult> UserInvoices(string cryptoCode, CancellationToken cancellationToken = default)
    {
        var network = _btcPayNetworkProvider.GetNetwork<BTCPayNetwork>(cryptoCode);
        if (network == null)
        {
            throw ErrorCryptoCodeNotFound();
        }
        
        var lightningClient = await GetLightningClient(cryptoCode, false);
        var invoices = await lightningClient.ListInvoices(cancellationToken);
        var userInvoices = invoices.Select(i =>
        {
            var bolt11 = BOLT11PaymentRequest.Parse(i.BOLT11, network.NBitcoinNetwork);
            return ToInvoiceData(i, bolt11);
        });
        return Ok(userInvoices);
    }

    // https://github.com/BlueWallet/LndHub/blob/master/doc/Send-requirements.md#get-getbalance
    [HttpGet("balance")]
    [Authorize(Policy = Policies.CanUseLightningNodeInStore, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
    public async Task<IActionResult> Balance(string cryptoCode, CancellationToken cancellationToken = default)
    {
        var lightningClient = await GetLightningClient(cryptoCode, true);
        var balance = await lightningClient.GetBalance(cancellationToken);
        var btc = new BtcBalance { AvailableBalance = balance.OffchainBalance?.Local };
        var result = new BalanceData { BTC = btc };

        return Ok(result);
    }

    // https://github.com/BlueWallet/LndHub/blob/master/doc/Send-requirements.md#get-decodeinvoice
    [HttpGet("decodeinvoice")]
    public IActionResult DecodeInvoice(string cryptoCode, [FromQuery] string invoice, CancellationToken cancellationToken = default)
    {
        var network = _btcPayNetworkProvider.GetNetwork<BTCPayNetwork>(cryptoCode);
        if (network == null)
        {
            throw ErrorCryptoCodeNotFound();
        }
        
        try
        {
            var bolt11 = BOLT11PaymentRequest.Parse(invoice, network.NBitcoinNetwork);
            var decoded = new DecodeInvoiceData
            {
                Destination = bolt11.GetPayeePubKey().ToString(),
                PaymentHash = bolt11.PaymentHash?.ToString(),
                Amount = bolt11.MinimumAmount,
                Timestamp = bolt11.Timestamp,
                Expiry = bolt11.ExpiryDate - bolt11.Timestamp,
                Description = bolt11.ShortDescription,
                DescriptionHash = bolt11.DescriptionHash
            };

            return Ok(decoded);
        }
        catch (Exception ex)
        {
            return Ok(new ErrorResponse(4, ex.Message));
        }
    }

    // https://github.com/getAlby/lightning-browser-extension/blob/f0b0ab9ad0b2dd6e60b864548fa39091ef81bbdc/src/extension/background-script/connectors/lndhub.ts#L249
    [HttpGet("checkpayment/{paymentHash}")]
    [Authorize(Policy = Policies.CanUseLightningNodeInStore, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
    public async Task<IActionResult> CheckPayment(string cryptoCode, string paymentHash, CancellationToken cancellationToken = default)
    {
        var lightningClient = await GetLightningClient(cryptoCode, false);
        var result = new CheckPaymentResponse { Paid = false };
        try
        {
            var payment = await lightningClient.GetPayment(paymentHash, cancellationToken);
            result.Paid = payment.Status == LightningPaymentStatus.Complete;
            return Ok(result);
        }
        catch (Exception)
        {
            return NotFound(result);
        }
    }

    // https://github.com/BlueWallet/LndHub/blob/master/doc/Send-requirements.md#post-addinvoice
    [HttpPost("addinvoice")]
    [Authorize(Policy = Policies.CanCreateLightningInvoiceInStore, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
    public async Task<IActionResult> AddInvoice(string cryptoCode, CreateInvoiceRequest request, CancellationToken cancellationToken = default)
    {
        var network = _btcPayNetworkProvider.GetNetwork<BTCPayNetwork>(cryptoCode);
        if (network == null)
        {
            throw ErrorCryptoCodeNotFound();
        }
        
        var lightningClient = await GetLightningClient(cryptoCode, false);
        if (request.Amount < LightMoney.Zero)
        {
            return Ok(new ErrorResponse(4, "Amount should be more or equals to 0"));
        }
        try
        {
            var desc = request.DescriptionHash != null ? request.DescriptionHash.ToString() : request.Memo;
            var param = new CreateInvoiceParams(request.Amount, desc, TimeSpan.FromDays(1))
            {
                PrivateRouteHints = true,
                DescriptionHashOnly = request.DescriptionHash is not null
            };
            var invoice = await lightningClient.CreateInvoice(param, cancellationToken);
            var bolt11 = BOLT11PaymentRequest.Parse(invoice.BOLT11, network.NBitcoinNetwork);
            var resp = ToInvoiceData(invoice, bolt11);

            return Ok(resp);
        }
        catch (Exception ex)
        {
            return Ok(new ErrorResponse(4, ex.Message));
        }
    }

    // https://github.com/BlueWallet/LndHub/blob/master/doc/Send-requirements.md#post-payinvoice
    [HttpPost("payinvoice")]
    [Authorize(Policy = Policies.CanUseLightningNodeInStore, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
    public async Task<IActionResult> PayInvoice(string cryptoCode, PayInvoiceRequest request, CancellationToken cancellationToken = default)
    {
        var lightningClient = await GetLightningClient(cryptoCode, true);
        var network = _btcPayNetworkProvider.GetNetwork<BTCPayNetwork>(cryptoCode);

        if (string.IsNullOrEmpty(request.PaymentRequest) ||
            !BOLT11PaymentRequest.TryParse(request.PaymentRequest, out BOLT11PaymentRequest bolt11, network.NBitcoinNetwork))
        {
            return Ok(new ErrorResponse(4, "The BOLT11 invoice was invalid."));
        }

        var param = request.Amount != null ? new PayInvoiceParams { Amount = request.Amount } : null;

        try
        {
            var payResponse = await lightningClient.Pay(request.PaymentRequest, param, cancellationToken);
            return payResponse.Result switch
            {
                PayResult.CouldNotFindRoute => Ok(new ErrorResponse(5, payResponse.ErrorDetail ?? "Impossible to find a route to the peer")),
                PayResult.Unknown => Ok(new ErrorResponse(7, payResponse.ErrorDetail ?? "Payment timed out, status unknown")),
                PayResult.Error => Ok(new ErrorResponse(7, payResponse.ErrorDetail)),
                PayResult.Ok => Ok(ToPaymentResponse(payResponse.Details, bolt11)),
                _ => throw new NotSupportedException("Unsupported PayResult")
            };
        }
        catch (Exception ex)
        {
            return Ok(new ErrorResponse(4, ex.Message));
        }
    }

    /* TODO: We could implement this endpoint too
    //https://github.com/BlueWallet/LndHub/blob/master/doc/Send-requirements.md#get-getbtc
    [Authorize(Policy = Policies.CanUseLightningNodeInStore, AuthenticationSchemes = AuthenticationSchemes.Greenfield)]
    [HttpPost("~/api/v1/stores/{storeId}/lightning/{cryptoCode}/address")]
    public override Task<IActionResult> GetDepositAddress(string cryptoCode, CancellationToken cancellationToken = default)
    {
        return base.GetDepositAddress(cryptoCode, cancellationToken);
    }
    */

    private InvoiceData ToInvoiceData(LightningInvoice t, BOLT11PaymentRequest bolt11)
    {
        var expireTime = TimeSpan.FromSeconds((bolt11.ExpiryDate - DateTime.Now).TotalSeconds);
        return new InvoiceData
        {
            Id = bolt11.Hash,
            Description = bolt11.ShortDescription,
            AddIndex = Convert.ToInt32(t.PaidAt?.ToUnixTimeSeconds()), // fake it
            PaymentHash = bolt11.PaymentHash?.ToString(),
            PaymentRequest = t.BOLT11,
            IsPaid = t.Status == LightningInvoiceStatus.Paid,
            ExpireTime = expireTime,
            Amount = t.Amount,
            CreatedAt = bolt11.Timestamp
        };
    }

    private TransactionData ToTransactionData(LightningPayment t, BTCPayNetwork network)
    {
        var bolt11 = BOLT11PaymentRequest.Parse(t.BOLT11, network.NBitcoinNetwork);
        return new TransactionData
        {
            PaymentHash = string.IsNullOrEmpty(t.PaymentHash) ? null : uint256.Parse(t.PaymentHash),
            PaymentPreimage = t.Preimage,
            Fee = t.Fee,
            Value = t.Amount,
            Timestamp = t.CreatedAt,
            Memo = bolt11.ShortDescription
        };
    }

    private PaymentResponse ToPaymentResponse(PayDetails t, BOLT11PaymentRequest bolt11)
    {
        var error = t.Status switch
        {
            LightningPaymentStatus.Failed => "Payment failed",
            LightningPaymentStatus.Pending => "Payment pending",
            LightningPaymentStatus.Unknown => "Payment status unknown",
            _ => "" // needs to be an empty string for compatibility across wallets
        };
        var expireTime = TimeSpan.FromSeconds((bolt11.ExpiryDate - DateTime.Now).TotalSeconds);
        return new PaymentResponse
        {
            PaymentError = error,
            PaymentRequest = bolt11.ToString(),
            PaymentPreimage = t.Preimage,
            PaymentHash = t.PaymentHash,
            Decoded = new PaymentData
            {
                PaymentPreimage = t.Preimage,
                PaymentHash = t.PaymentHash,
                Destination = bolt11.GetPayeePubKey().ToString(),
                Amount = t.TotalAmount,
                Description = bolt11.ShortDescription,
                DescriptionHash = bolt11.DescriptionHash?.ToString(),
                ExpireTime = expireTime,
                Timestamp = bolt11.Timestamp
            },
            PaymentRoute = new PaymentRoute
            {
                Amount = t.TotalAmount,
                Fee = t.FeeAmount
            }
        };
    }

    protected override Task<ILightningClient> GetLightningClient(string cryptoCode, bool doingAdminThings)
    {
        var network = _btcPayNetworkProvider.GetNetwork<BTCPayNetwork>(cryptoCode);
        if (network == null)
        {
            throw ErrorCryptoCodeNotFound();
        }

        var store = HttpContext.GetStoreData();
        if (store == null)
        {
            throw new JsonHttpException(StoreNotFound());
        }

        var id = new PaymentMethodId(cryptoCode, PaymentTypes.LightningLike);
        var existing = store.GetSupportedPaymentMethods(_btcPayNetworkProvider)
            .OfType<LightningSupportedPaymentMethod>()
            .FirstOrDefault(d => d.PaymentId == id);
        if (existing == null)
            throw ErrorLightningNodeNotConfiguredForStore();
        if (existing.GetExternalLightningUrl() is { } connectionString)
        {
            return Task.FromResult(_lightningClientFactory.Create(connectionString, network));
        }
        if (existing.IsInternalNode &&
             _lightningNetworkOptions.Value.InternalLightningByCryptoCode.TryGetValue(network.CryptoCode,
                 out var internalLightningNode))
        {
            if (!User.IsInRole(Roles.ServerAdmin) && doingAdminThings)
            {
                throw ErrorShouldBeAdminForInternalNode();
            }
            return Task.FromResult(_lightningClientFactory.Create(internalLightningNode, network));
        }
        throw ErrorLightningNodeNotConfiguredForStore();
    }

    private IActionResult StoreNotFound()
    {
        return this.CreateAPIError(404, "store-not-found", "The store was not found");
    }
}
