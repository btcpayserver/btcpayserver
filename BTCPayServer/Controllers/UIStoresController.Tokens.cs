#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Models;
using BTCPayServer.Models.StoreViewModels;
using BTCPayServer.Security.Bitpay;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using NBitcoin;
using NBitcoin.DataEncoders;

namespace BTCPayServer.Controllers;

public partial class UIStoresController
{

    [HttpGet("{storeId}/tokens")]
    [Authorize(Policy = Policies.CanViewStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> ListTokens()
    {
        var model = new TokensViewModel();
        var tokens = await tokenRepo.GetTokensByStoreIdAsync(CurrentStore.Id);
        model.StoreNotConfigured = StoreNotConfigured;
        model.Tokens = tokens.Select(t => new TokenViewModel
        {
            Label = t.Label,
            SIN = t.SIN,
            Id = t.Value
        }).ToArray();

        model.ApiKey = (await tokenRepo.GetLegacyAPIKeys(CurrentStore.Id)).FirstOrDefault();
        if (model.ApiKey == null)
            model.EncodedApiKey = "*API Key*";
        else
            model.EncodedApiKey = Encoders.Base64.EncodeData(Encoders.ASCII.DecodeData(model.ApiKey));
        return View(model);
    }

    [HttpGet("{storeId}/tokens/{tokenId}/revoke")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> RevokeToken(string tokenId)
    {
        var token = await tokenRepo.GetToken(tokenId);
        if (token == null || token.StoreId != CurrentStore.Id)
            return NotFound();
        return View("Confirm", new ConfirmModel("Revoke the token", $"The access token with the label <strong>{Html.Encode(token.Label)}</strong> will be revoked. Do you wish to continue?", "Revoke"));
    }

    [HttpPost("{storeId}/tokens/{tokenId}/revoke")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> RevokeTokenConfirm(string tokenId)
    {
        var token = await tokenRepo.GetToken(tokenId);
        if (token == null ||
            token.StoreId != CurrentStore.Id ||
            !await tokenRepo.DeleteToken(tokenId))
            TempData[WellKnownTempData.ErrorMessage] = "Failure to revoke this token.";
        else
            TempData[WellKnownTempData.SuccessMessage] = "Token revoked";
        return RedirectToAction(nameof(ListTokens), new { storeId = token?.StoreId });
    }

    [HttpGet("{storeId}/tokens/{tokenId}")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> ShowToken(string tokenId)
    {
        var token = await tokenRepo.GetToken(tokenId);
        if (token == null || token.StoreId != CurrentStore.Id)
            return NotFound();
        return View(token);
    }

    [HttpGet("{storeId}/tokens/create")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public IActionResult CreateToken(string storeId)
    {
        var model = new CreateTokenViewModel();
        ViewBag.HidePublicKey = storeId == null;
        ViewBag.ShowStores = storeId == null;
        ViewBag.ShowMenu = storeId != null;
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
        model.Label = model.Label ?? String.Empty;
        var userId = GetUserId();
        if (userId == null)
            return Challenge(AuthenticationSchemes.Cookie);
        var store = model.StoreId switch
        {
            null => CurrentStore,
            _ => await repo.FindStore(storeId, userId)
        };
        if (store == null)
            return Challenge(AuthenticationSchemes.Cookie);
        var tokenRequest = new TokenRequest
        {
            Label = model.Label,
            Id = model.PublicKey == null ? null : NBitpayClient.Extensions.BitIdExtensions.GetBitIDSIN(new PubKey(model.PublicKey).Compress())
        };

        string? pairingCode = null;
        if (model.PublicKey == null)
        {
            tokenRequest.PairingCode = await tokenRepo.CreatePairingCodeAsync();
            await tokenRepo.UpdatePairingCode(new PairingCodeEntity()
            {
                Id = tokenRequest.PairingCode,
                Label = model.Label,
            });
            await tokenRepo.PairWithStoreAsync(tokenRequest.PairingCode, store.Id);
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
        ViewBag.ShowMenu = false;
        var stores = (await repo.GetStoresByUserId(userId)).Where(data => data.HasPermission(userId, Policies.CanModifyStoreSettings)).ToArray();

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
            await tokenRepo.RevokeLegacyAPIKeys(CurrentStore.Id);
            TempData[WellKnownTempData.SuccessMessage] = "API Key revoked";
        }
        else
        {
            await tokenRepo.GenerateLegacyAPIKey(CurrentStore.Id);
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

        if (pairingCode == null)
            return NotFound();

        if (selectedStore != null)
        {
            var store = await repo.FindStore(selectedStore, userId);
            if (store == null)
                return NotFound();
            HttpContext.SetStoreData(store);
        }

        var pairing = await tokenRepo.GetPairingAsync(pairingCode);
        if (pairing == null)
        {
            TempData[WellKnownTempData.ErrorMessage] = "Unknown pairing code";
            return RedirectToAction(nameof(UIHomeController.Index), "UIHome");
        }

        var stores = (await repo.GetStoresByUserId(userId)).Where(data => data.HasPermission(userId, Policies.CanModifyStoreSettings)).ToArray();
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
        if (pairingCode == null)
            return NotFound();
        var store = CurrentStore;
        var pairing = await tokenRepo.GetPairingAsync(pairingCode);
        if (store == null || pairing == null)
            return NotFound();

        var pairingResult = await tokenRepo.PairWithStoreAsync(pairingCode, store.Id);
        if (pairingResult is PairingResult.Complete or PairingResult.Partial)
        {
            var excludeFilter = store.GetStoreBlob().GetExcludedPaymentMethods();
            StoreNotConfigured = store.GetSupportedPaymentMethods(networkProvider).All(p => excludeFilter.Match(p.PaymentId));
            TempData[WellKnownTempData.SuccessMessage] = "Pairing is successful";
            if (pairingResult == PairingResult.Partial)
                TempData[WellKnownTempData.SuccessMessage] = "Server initiated pairing code: " + pairingCode;
            return RedirectToAction(nameof(ListTokens), new
            {
                storeId = store.Id,
                pairingCode
            });
        }

        TempData[WellKnownTempData.ErrorMessage] = $"Pairing failed ({pairingResult})";
        return RedirectToAction(nameof(ListTokens), new
        {
            storeId = store.Id
        });
    }
}
