#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Controllers;
using BTCPayServer.Data;
using BTCPayServer.Models;
using BTCPayServer.Plugins.Bitpay.Security;
using BTCPayServer.Plugins.Bitpay.Views;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Localization;
using NBitcoin;
using NBitcoin.DataEncoders;

namespace BTCPayServer.Plugins.Bitpay.Controllers;

[Route("stores")]
[Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
[Authorize(Policy = Policies.CanViewStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
[Area(BitpayPlugin.Area)]
public class UIStoresTokenController(
    TokenRepository tokenRepository,
    BitpayAccessTokenController tokenController,
    IStringLocalizer stringLocalizer,
    StoreRepository storeRepository,
    UserManager<ApplicationUser> userManager,
    IHtmlHelper html,
    PaymentMethodHandlerDictionary handlers) : Controller
{
    public IStringLocalizer StringLocalizer { get; } = stringLocalizer;
    public StoreData CurrentStore => HttpContext.GetStoreData() ?? throw new InvalidOperationException("Store not found");
    private string? GetUserId() => userManager.GetUserId(User);

    [TempData]
    public bool StoreNotConfigured { get; set; }
    public string? GeneratedPairingCode { get; set; }
    [HttpGet("{storeId}/tokens")]
    [Authorize(Policy = Policies.CanViewStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> ListTokens()
    {
        var model = new TokensViewModel();
        var tokens = await tokenRepository.GetTokensByStoreIdAsync(CurrentStore.Id);
        model.StoreNotConfigured = StoreNotConfigured;
        model.Tokens = tokens.Select(t => new TokenViewModel()
        {
            Label = t.Label,
            SIN = t.SIN,
            Id = t.Value
        }).ToArray();

        model.ApiKey = (await tokenRepository.GetLegacyAPIKeys(CurrentStore.Id)).FirstOrDefault();
        model.EncodedApiKey = model.ApiKey == null ? "*API Key*" : Encoders.Base64.EncodeData(Encoders.ASCII.DecodeData(model.ApiKey));
        return View(model);
    }

    [HttpGet("{storeId}/tokens/{tokenId}/revoke")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> RevokeToken(string tokenId)
    {
        var token = await tokenRepository.GetToken(tokenId);
        if (token == null || token.StoreId != CurrentStore.Id)
            return NotFound();
        return View("Confirm", new ConfirmModel(StringLocalizer["Revoke the token"], $"The access token with the label <strong>{html.Encode(token.Label)}</strong> will be revoked. Do you wish to continue?", "Revoke"));
    }

    [HttpPost("{storeId}/tokens/{tokenId}/revoke")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> RevokeTokenConfirm(string tokenId)
    {
        var token = await tokenRepository.GetToken(tokenId);
        if (token == null ||
            token.StoreId != CurrentStore.Id ||
            !await tokenRepository.DeleteToken(tokenId))
            TempData[WellKnownTempData.ErrorMessage] = "Failure to revoke this token.";
        else
            TempData[WellKnownTempData.SuccessMessage] = "Token revoked";
        return RedirectToAction(nameof(ListTokens), new { storeId = token?.StoreId });
    }

    [HttpGet("{storeId}/tokens/{tokenId}")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> ShowToken(string tokenId)
    {
        var token = await tokenRepository.GetToken(tokenId);
        if (token == null || token.StoreId != CurrentStore.Id)
            return NotFound();
        return View(token);
    }

    [HttpGet("{storeId}/tokens/create")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public IActionResult CreateToken(string storeId)
    {
        var model = new CreateTokenViewModel();
        ViewBag.HidePublicKey = false;
        ViewBag.ShowStores = false;
        model.StoreId = storeId;
        return View(model);
    }

    [HttpPost("{storeId}/tokens/create")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> CreateToken(string storeId, CreateTokenViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(nameof(CreateToken), model);
        }
        model.Label ??= string.Empty;
        var userId = GetUserId();
        if (userId == null)
            return Challenge(AuthenticationSchemes.Cookie);
        var store = model.StoreId switch
        {
            null => CurrentStore,
            _ => await storeRepository.FindStore(storeId, userId)
        };
        if (store == null)
            return Challenge(AuthenticationSchemes.Cookie);
        var tokenRequest = new TokenRequest()
        {
            Label = model.Label,
            Id = model.PublicKey == null ? null : NBitpayClient.Extensions.BitIdExtensions.GetBitIDSIN(new PubKey(model.PublicKey).Compress())
        };

        string? pairingCode;
        if (model.PublicKey == null)
        {
            tokenRequest.PairingCode = await tokenRepository.CreatePairingCodeAsync();
            await tokenRepository.UpdatePairingCode(new PairingCodeEntity()
            {
                Id = tokenRequest.PairingCode,
                Label = model.Label,
            });
            await tokenRepository.PairWithStoreAsync(tokenRequest.PairingCode, store.Id);
            pairingCode = tokenRequest.PairingCode;
        }
        else
        {
            pairingCode = (await tokenController.Tokens(tokenRequest)).Data[0].PairingCode;
        }

        GeneratedPairingCode = pairingCode;
        return RedirectToAction(nameof(RequestPairing), new
        {
            pairingCode,
            selectedStore = storeId
        });
    }

    [HttpGet("/api-tokens")]
    [AllowAnonymous]
    public async Task<IActionResult> CreateToken()
    {
        var userId = GetUserId();
        if (string.IsNullOrWhiteSpace(userId))
            return Challenge(AuthenticationSchemes.Cookie);
        var model = new CreateTokenViewModel();
        ViewBag.HidePublicKey = true;
        ViewBag.ShowStores = true;
        var stores = (await storeRepository.GetStoresByUserId(userId)).Where(data => data.HasPermission(userId, Policies.CanModifyStoreSettings)).ToArray();

        model.Stores = new SelectList(stores, nameof(CurrentStore.Id), nameof(CurrentStore.StoreName));
        if (!model.Stores.Any())
        {
            TempData[WellKnownTempData.ErrorMessage] = "You need to be owner of at least one store before pairing";
            return RedirectToAction(nameof(UIHomeController.Index), "UIHome");
        }
        return View(model);
    }

    [HttpPost("/api-tokens")]
    [AllowAnonymous]
    public Task<IActionResult> CreateToken2(CreateTokenViewModel model)
    {
        return CreateToken(model.StoreId, model);
    }

    [HttpPost("{storeId}/tokens/apikey")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> GenerateAPIKey(string storeId, string command = "")
    {
        var store = HttpContext.GetStoreData();
        if (store == null)
            return NotFound();
        if (command == "revoke")
        {
            await tokenRepository.RevokeLegacyAPIKeys(CurrentStore.Id);
            TempData[WellKnownTempData.SuccessMessage] = "API Key revoked";
        }
        else
        {
            await tokenRepository.GenerateLegacyAPIKey(CurrentStore.Id);
            TempData[WellKnownTempData.SuccessMessage] = "API Key re-generated";
        }

        return RedirectToAction(nameof(ListTokens), new
        {
            storeId
        });
    }

    [HttpGet("/api-access-request")]
    [AllowAnonymous]
    public async Task<IActionResult> RequestPairing(string pairingCode, string? selectedStore = null)
    {
        var userId = GetUserId();
        if (userId == null)
            return Challenge(AuthenticationSchemes.Cookie);

        if (selectedStore != null)
        {
            var store = await storeRepository.FindStore(selectedStore, userId);
            if (store == null)
                return NotFound();
            HttpContext.SetStoreData(store);
        }

        var pairing = await tokenRepository.GetPairingAsync(pairingCode);
        if (pairing == null)
        {
            TempData[WellKnownTempData.ErrorMessage] = "Unknown pairing code";
            return RedirectToAction(nameof(UIHomeController.Index), "UIHome");
        }

        var stores = (await storeRepository.GetStoresByUserId(userId)).Where(data => data.HasPermission(userId, Policies.CanModifyStoreSettings)).ToArray();
        return View(new PairingModel
        {
            Id = pairing.Id,
            Label = pairing.Label,
            SIN = pairing.SIN ?? "Server-Initiated Pairing",
            StoreId = selectedStore ?? stores.FirstOrDefault()?.Id,
            Stores = stores.Select(s => new PairingModel.StoreViewModel
            {
                Id = s.Id,
                Name = string.IsNullOrEmpty(s.StoreName) ? s.Id : s.StoreName
            }).ToArray()
        });
    }

    [HttpPost("/api-access-request")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> Pair(string pairingCode, string storeId)
    {
        var store = CurrentStore;
        var pairing = await tokenRepository.GetPairingAsync(pairingCode);
        if (pairing == null || store.Id != storeId)
            return NotFound();

        var pairingResult = await tokenRepository.PairWithStoreAsync(pairingCode, store.Id);
        if (pairingResult == PairingResult.Complete || pairingResult == PairingResult.Partial)
        {
            var excludeFilter = store.GetStoreBlob().GetExcludedPaymentMethods();
            StoreNotConfigured = store.GetPaymentMethodConfigs(handlers).All(p => excludeFilter.Match(p.Key));
            TempData[WellKnownTempData.SuccessMessage] = "Pairing is successful";
            if (pairingResult == PairingResult.Partial)
                TempData[WellKnownTempData.SuccessMessage] = $"Server initiated pairing code: {pairingCode}";
            return RedirectToAction(nameof(ListTokens), new
            {
                storeId = store.Id, pairingCode
            });
        }

        TempData[WellKnownTempData.ErrorMessage] = $"Pairing failed: {pairingResult}";
        return RedirectToAction(nameof(ListTokens), new
        {
            storeId = store.Id
        });
    }
}
