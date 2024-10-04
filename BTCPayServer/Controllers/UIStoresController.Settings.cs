#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Models.StoreViewModels;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Lightning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace BTCPayServer.Controllers;

public partial class UIStoresController
{
    [HttpGet("{storeId}/settings")]
    public async Task<IActionResult> GeneralSettings(string storeId)
    {
        var store = HttpContext.GetStoreData();
        if (store == null) return NotFound();

        var storeBlob = store.GetStoreBlob();
        var vm = new GeneralSettingsViewModel
        {
            Id = store.Id,
            StoreName = store.StoreName,
            StoreWebsite = store.StoreWebsite,
            LogoUrl = await _uriResolver.Resolve(Request.GetAbsoluteRootUri(), storeBlob.LogoUrl),
            CssUrl = await _uriResolver.Resolve(Request.GetAbsoluteRootUri(), storeBlob.CssUrl),
            BrandColor = storeBlob.BrandColor,
            ApplyBrandColorToBackend = storeBlob.ApplyBrandColorToBackend,
            NetworkFeeMode = storeBlob.NetworkFeeMode,
            AnyoneCanCreateInvoice = storeBlob.AnyoneCanInvoice,
            PaymentTolerance = storeBlob.PaymentTolerance,
            InvoiceExpiration = (int)storeBlob.InvoiceExpiration.TotalMinutes,
            DefaultCurrency = storeBlob.DefaultCurrency,
            BOLT11Expiration = (long)storeBlob.RefundBOLT11Expiration.TotalDays,
            Archived = store.Archived,
            MonitoringExpiration = (int)storeBlob.MonitoringExpiration.TotalMinutes,
            SpeedPolicy = store.SpeedPolicy,
            ShowRecommendedFee = storeBlob.ShowRecommendedFee,
            RecommendedFeeBlockTarget = storeBlob.RecommendedFeeBlockTarget
        };

        return View(vm);
    }

    [HttpPost("{storeId}/settings")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> GeneralSettings(
        GeneralSettingsViewModel model,
        [FromForm] bool RemoveLogoFile = false,
        [FromForm] bool RemoveCssFile = false)
    {
        bool needUpdate = false;
        if (CurrentStore.StoreName != model.StoreName)
        {
            needUpdate = true;
            CurrentStore.StoreName = model.StoreName;
        }

        if (CurrentStore.StoreWebsite != model.StoreWebsite)
        {
            needUpdate = true;
            CurrentStore.StoreWebsite = model.StoreWebsite;
        }

        if (CurrentStore.SpeedPolicy != model.SpeedPolicy)
        {
            CurrentStore.SpeedPolicy = model.SpeedPolicy;
            needUpdate = true;
        }

        var blob = CurrentStore.GetStoreBlob();
        blob.AnyoneCanInvoice = model.AnyoneCanCreateInvoice;
        blob.NetworkFeeMode = model.NetworkFeeMode;
        blob.PaymentTolerance = model.PaymentTolerance;
        blob.DefaultCurrency = model.DefaultCurrency;
        blob.ShowRecommendedFee = model.ShowRecommendedFee;
        blob.RecommendedFeeBlockTarget = model.RecommendedFeeBlockTarget;
        blob.InvoiceExpiration = TimeSpan.FromMinutes(model.InvoiceExpiration);
        blob.RefundBOLT11Expiration = TimeSpan.FromDays(model.BOLT11Expiration);
        blob.MonitoringExpiration = TimeSpan.FromMinutes(model.MonitoringExpiration);
        if (!string.IsNullOrEmpty(model.BrandColor) && !ColorPalette.IsValid(model.BrandColor))
        {
            ModelState.AddModelError(nameof(model.BrandColor), "The brand color needs to be a valid hex color code");
            return View(model);
        }
        blob.BrandColor = model.BrandColor;
        blob.ApplyBrandColorToBackend = model.ApplyBrandColorToBackend && !string.IsNullOrEmpty(model.BrandColor);

        var userId = GetUserId();
        if (userId is null)
            return NotFound();

        if (model.LogoFile != null)
        {
            if (model.LogoFile.Length > 1_000_000)
            {
                ModelState.AddModelError(nameof(model.LogoFile), "The uploaded logo file should be less than 1MB");
            }
            else if (!model.LogoFile.ContentType.StartsWith("image/", StringComparison.InvariantCulture))
            {
                ModelState.AddModelError(nameof(model.LogoFile), "The uploaded logo file needs to be an image");
            }
            else
            {
                var formFile = await model.LogoFile.Bufferize();
                if (!FileTypeDetector.IsPicture(formFile.Buffer, formFile.FileName))
                {
                    ModelState.AddModelError(nameof(model.LogoFile), "The uploaded logo file needs to be an image");
                }
                else
                {
                    model.LogoFile = formFile;
                    // add new image
                    try
                    {
                        var storedFile = await _fileService.AddFile(model.LogoFile, userId);
                        blob.LogoUrl = new UnresolvedUri.FileIdUri(storedFile.Id);
                    }
                    catch (Exception e)
                    {
                        ModelState.AddModelError(nameof(model.LogoFile), $"Could not save logo: {e.Message}");
                    }
                }
            }
        }
        else if (RemoveLogoFile && blob.LogoUrl is not null)
        {
            blob.LogoUrl = null;
            needUpdate = true;
        }

        if (model.CssFile != null)
        {
            if (model.CssFile.Length > 1_000_000)
            {
                ModelState.AddModelError(nameof(model.CssFile), "The uploaded file should be less than 1MB");
            }
            else if (!model.CssFile.ContentType.Equals("text/css", StringComparison.InvariantCulture))
            {
                ModelState.AddModelError(nameof(model.CssFile), "The uploaded file needs to be a CSS file");
            }
            else if (!model.CssFile.FileName.EndsWith(".css", StringComparison.OrdinalIgnoreCase))
            {
                ModelState.AddModelError(nameof(model.CssFile), "The uploaded file needs to be a CSS file");
            }
            else
            {
                // add new file
                try
                {
                    var storedFile = await _fileService.AddFile(model.CssFile, userId);
                    blob.CssUrl = new UnresolvedUri.FileIdUri(storedFile.Id);
                }
                catch (Exception e)
                {
                    ModelState.AddModelError(nameof(model.CssFile), $"Could not save CSS file: {e.Message}");
                }
            }
        }
        else if (RemoveCssFile && blob.CssUrl is not null)
        {
            blob.CssUrl = null;
            needUpdate = true;
        }

        if (CurrentStore.SetStoreBlob(blob))
        {
            needUpdate = true;
        }

        if (needUpdate)
        {
            await _storeRepo.UpdateStore(CurrentStore);

            TempData[WellKnownTempData.SuccessMessage] = "Store successfully updated";
        }

        return RedirectToAction(nameof(GeneralSettings), new
        {
            storeId = CurrentStore.Id
        });
    }
        
    [HttpPost("{storeId}/archive")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> ToggleArchive(string storeId)
    {
        CurrentStore.Archived = !CurrentStore.Archived;
        await _storeRepo.UpdateStore(CurrentStore);

        TempData[WellKnownTempData.SuccessMessage] = CurrentStore.Archived
            ? "The store has been archived and will no longer appear in the stores list by default."
            : "The store has been unarchived and will appear in the stores list by default again.";

        return RedirectToAction(nameof(GeneralSettings), new
        {
            storeId = CurrentStore.Id
        });
    }

    [HttpGet("{storeId}/delete")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public IActionResult DeleteStore(string storeId)
    {
        return View("Confirm", new ConfirmModel("Delete store", "The store will be permanently deleted. This action will also delete all invoices, apps and data associated with the store. Are you sure?", "Delete"));
    }

    [HttpPost("{storeId}/delete")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> DeleteStorePost(string storeId)
    {
        await _storeRepo.DeleteStore(CurrentStore.Id);
        TempData[WellKnownTempData.SuccessMessage] = "Store successfully deleted.";
        return RedirectToAction(nameof(UIHomeController.Index), "UIHome");
    }
        
    [HttpGet("{storeId}/checkout")]
    public async Task<IActionResult> CheckoutAppearance()
    {
        var storeBlob = CurrentStore.GetStoreBlob();
        var vm = new CheckoutAppearanceViewModel();
        SetCryptoCurrencies(vm, CurrentStore);
        vm.PaymentMethodCriteria = CurrentStore.GetPaymentMethodConfigs(_handlers)
            .Where(s => !storeBlob.GetExcludedPaymentMethods().Match(s.Key) && s.Value is not LNURLPaymentMethodConfig)
            .Select(c =>
            {
                var pmi = c.Key;
                var existing = storeBlob.PaymentMethodCriteria.SingleOrDefault(criteria =>
                    criteria.PaymentMethod == pmi);
                return existing is null
                    ? new PaymentMethodCriteriaViewModel { PaymentMethod = pmi.ToString(), Value = "" }
                    : new PaymentMethodCriteriaViewModel
                    {
                        PaymentMethod = existing.PaymentMethod.ToString(),
                        Type = existing.Above
                            ? PaymentMethodCriteriaViewModel.CriteriaType.GreaterThan
                            : PaymentMethodCriteriaViewModel.CriteriaType.LessThan,
                        Value = existing.Value?.ToString() ?? ""
                    };
            }).ToList();

        vm.CelebratePayment = storeBlob.CelebratePayment;
        vm.PlaySoundOnPayment = storeBlob.PlaySoundOnPayment;
        vm.OnChainWithLnInvoiceFallback = storeBlob.OnChainWithLnInvoiceFallback;
        vm.ShowPayInWalletButton = storeBlob.ShowPayInWalletButton;
        vm.ShowStoreHeader = storeBlob.ShowStoreHeader;
        vm.LightningAmountInSatoshi = storeBlob.LightningAmountInSatoshi;
        vm.LazyPaymentMethods = storeBlob.LazyPaymentMethods;
        vm.RedirectAutomatically = storeBlob.RedirectAutomatically;
        vm.PaymentSoundUrl = storeBlob.PaymentSoundUrl is null
            ? string.Concat(Request.GetAbsoluteRootUri().ToString(), "checkout/payment.mp3")
            : await _uriResolver.Resolve(Request.GetAbsoluteRootUri(), storeBlob.PaymentSoundUrl);
        vm.HtmlTitle = storeBlob.HtmlTitle;
        vm.SupportUrl = storeBlob.StoreSupportUrl;
        vm.DisplayExpirationTimer = (int)storeBlob.DisplayExpirationTimer.TotalMinutes;
        vm.ReceiptOptions = CheckoutAppearanceViewModel.ReceiptOptionsViewModel.Create(storeBlob.ReceiptOptions);
        vm.AutoDetectLanguage = storeBlob.AutoDetectLanguage;
        vm.SetLanguages(_langService, storeBlob.DefaultLang);

        return View(vm);
    }

    [HttpPost("{storeId}/checkout")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> CheckoutAppearance(CheckoutAppearanceViewModel model, [FromForm] bool RemoveSoundFile = false)
    {
        bool needUpdate = false;
        var blob = CurrentStore.GetStoreBlob();
        var defaultPaymentMethodId = model.DefaultPaymentMethod == null ? null : PaymentMethodId.Parse(model.DefaultPaymentMethod);
        if (CurrentStore.GetDefaultPaymentId() != defaultPaymentMethodId)
        {
            needUpdate = true;
            CurrentStore.SetDefaultPaymentId(defaultPaymentMethodId);
        }
        SetCryptoCurrencies(model, CurrentStore);
        model.SetLanguages(_langService, model.DefaultLang);
        model.PaymentMethodCriteria ??= new List<PaymentMethodCriteriaViewModel>();
        for (var index = 0; index < model.PaymentMethodCriteria.Count; index++)
        {
            var methodCriterion = model.PaymentMethodCriteria[index];
            if (!string.IsNullOrWhiteSpace(methodCriterion.Value))
            {
                if (!CurrencyValue.TryParse(methodCriterion.Value, out _))
                {
                    model.AddModelError(viewModel => viewModel.PaymentMethodCriteria[index].Value,
                        $"{methodCriterion.PaymentMethod}: Invalid format. Make sure to enter a valid amount and currency code. Examples: '5 USD', '0.001 BTC'", this);
                }
            }
        }
            
        var userId = GetUserId();
        if (userId is null)
            return NotFound();

        if (model.SoundFile != null)
        {
            if (model.SoundFile.Length > 1_000_000)
            {
                ModelState.AddModelError(nameof(model.SoundFile), "The uploaded sound file should be less than 1MB");
            }
            else if (!model.SoundFile.ContentType.StartsWith("audio/", StringComparison.InvariantCulture))
            {
                ModelState.AddModelError(nameof(model.SoundFile), "The uploaded sound file needs to be an audio file");
            }
            else
            {
                var formFile = await model.SoundFile.Bufferize();
                if (!FileTypeDetector.IsAudio(formFile.Buffer, formFile.FileName))
                {
                    ModelState.AddModelError(nameof(model.SoundFile), "The uploaded sound file needs to be an audio file");
                }
                else
                {
                    model.SoundFile = formFile;
                    // add new file
                    try
                    {
                        var storedFile = await _fileService.AddFile(model.SoundFile, userId);
                        blob.PaymentSoundUrl = new UnresolvedUri.FileIdUri(storedFile.Id);
                        needUpdate = true;
                    }
                    catch (Exception e)
                    {
                        ModelState.AddModelError(nameof(model.SoundFile), $"Could not save sound: {e.Message}");
                    }
                }
            }
        }
        else if (RemoveSoundFile && blob.PaymentSoundUrl is not null)
        {
            blob.PaymentSoundUrl = null;
            needUpdate = true;
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        // Payment criteria for Off-Chain should also affect LNUrl
        foreach (var newCriteria in model.PaymentMethodCriteria.ToList())
        {
            var paymentMethodId = PaymentMethodId.Parse(newCriteria.PaymentMethod);
            if (_handlers.TryGet(paymentMethodId) is LightningLikePaymentHandler h)
                model.PaymentMethodCriteria.Add(new PaymentMethodCriteriaViewModel
                {
                    PaymentMethod = PaymentTypes.LNURL.GetPaymentMethodId(h.Network.CryptoCode).ToString(),
                    Type = newCriteria.Type,
                    Value = newCriteria.Value
                });
            // Should not be able to set LNUrlPay criteria directly in UI
            if (_handlers.TryGet(paymentMethodId) is LNURLPayPaymentHandler)
                model.PaymentMethodCriteria.Remove(newCriteria);
        }
        blob.PaymentMethodCriteria ??= new List<PaymentMethodCriteria>();
        foreach (var newCriteria in model.PaymentMethodCriteria)
        {
            var paymentMethodId = PaymentMethodId.Parse(newCriteria.PaymentMethod);
            var existingCriteria = blob.PaymentMethodCriteria.FirstOrDefault(c => c.PaymentMethod == paymentMethodId);
            if (existingCriteria != null)
                blob.PaymentMethodCriteria.Remove(existingCriteria);
            CurrencyValue.TryParse(newCriteria.Value, out var cv);
            blob.PaymentMethodCriteria.Add(new PaymentMethodCriteria()
            {
                Above = newCriteria.Type == PaymentMethodCriteriaViewModel.CriteriaType.GreaterThan,
                Value = cv,
                PaymentMethod = paymentMethodId
            });
        }

        blob.ShowPayInWalletButton = model.ShowPayInWalletButton;
        blob.ShowStoreHeader = model.ShowStoreHeader;
        blob.CelebratePayment = model.CelebratePayment;
        blob.PlaySoundOnPayment = model.PlaySoundOnPayment;
        blob.OnChainWithLnInvoiceFallback = model.OnChainWithLnInvoiceFallback;
        blob.LightningAmountInSatoshi = model.LightningAmountInSatoshi;
        blob.LazyPaymentMethods = model.LazyPaymentMethods;
        blob.RedirectAutomatically = model.RedirectAutomatically;
        blob.ReceiptOptions = model.ReceiptOptions.ToDTO();
        blob.HtmlTitle = string.IsNullOrWhiteSpace(model.HtmlTitle) ? null : model.HtmlTitle;
        blob.StoreSupportUrl = string.IsNullOrWhiteSpace(model.SupportUrl) ? null : model.SupportUrl.IsValidEmail() ? $"mailto:{model.SupportUrl}" : model.SupportUrl;
        blob.DisplayExpirationTimer = TimeSpan.FromMinutes(model.DisplayExpirationTimer);
        blob.AutoDetectLanguage = model.AutoDetectLanguage;
        blob.DefaultLang = model.DefaultLang;
        if (CurrentStore.SetStoreBlob(blob))
        {
            needUpdate = true;
        }
        if (needUpdate)
        {
            await _storeRepo.UpdateStore(CurrentStore);
            TempData[WellKnownTempData.SuccessMessage] = "Store successfully updated";
        }

        return RedirectToAction(nameof(CheckoutAppearance), new
        {
            storeId = CurrentStore.Id
        });
    }

    void SetCryptoCurrencies(CheckoutAppearanceViewModel vm, StoreData storeData)
    {
        var choices = GetEnabledPaymentMethodChoices(storeData);
        var chosen = GetDefaultPaymentMethodChoice(storeData);

        vm.PaymentMethods = new SelectList(choices, nameof(chosen.Value), nameof(chosen.Name), chosen?.Value);
        vm.DefaultPaymentMethod = chosen?.Value;
    }

    PaymentMethodOptionViewModel.Format? GetDefaultPaymentMethodChoice(StoreData storeData)
    {
        var enabled = storeData.GetEnabledPaymentIds();
        var defaultPaymentId = storeData.GetDefaultPaymentId();
        var defaultChoice = defaultPaymentId?.FindNearest(enabled);
        if (defaultChoice is null)
        {
            defaultChoice = enabled.FirstOrDefault(e => e == PaymentTypes.CHAIN.GetPaymentMethodId(_networkProvider.DefaultNetwork.CryptoCode)) ??
                            enabled.FirstOrDefault(e => e == PaymentTypes.LN.GetPaymentMethodId(_networkProvider.DefaultNetwork.CryptoCode)) ??
                            enabled.FirstOrDefault();
        }
        var choices = GetEnabledPaymentMethodChoices(storeData);

        return defaultChoice is null ? null : choices.FirstOrDefault(c => defaultChoice.ToString().Equals(c.Value, StringComparison.OrdinalIgnoreCase));
    }
}
