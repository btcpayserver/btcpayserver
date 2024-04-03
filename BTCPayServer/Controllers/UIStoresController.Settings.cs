#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Models.StoreViewModels;
using BTCPayServer.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace BTCPayServer.Controllers;

public partial class UIStoresController
{
    [HttpGet("{storeId}/settings")]
    public IActionResult GeneralSettings()
    {
        var store = HttpContext.GetStoreData();
        if (store == null)
            return NotFound();

        var storeBlob = store.GetStoreBlob();
        var vm = new GeneralSettingsViewModel
        {
            Id = store.Id,
            StoreName = store.StoreName,
            StoreWebsite = store.StoreWebsite,
            LogoFileId = storeBlob.LogoFileId,
            CssFileId = storeBlob.CssFileId,
            BrandColor = storeBlob.BrandColor,
            NetworkFeeMode = storeBlob.NetworkFeeMode,
            AnyoneCanCreateInvoice = storeBlob.AnyoneCanInvoice,
            PaymentTolerance = storeBlob.PaymentTolerance,
            InvoiceExpiration = (int)storeBlob.InvoiceExpiration.TotalMinutes,
            DefaultCurrency = storeBlob.DefaultCurrency,
            BOLT11Expiration = (long)storeBlob.RefundBOLT11Expiration.TotalDays,
            Archived = store.Archived,
            CanDelete = repo.CanDeleteStores()
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

        var blob = CurrentStore.GetStoreBlob();
        blob.AnyoneCanInvoice = model.AnyoneCanCreateInvoice;
        blob.NetworkFeeMode = model.NetworkFeeMode;
        blob.PaymentTolerance = model.PaymentTolerance;
        blob.DefaultCurrency = model.DefaultCurrency;
        blob.InvoiceExpiration = TimeSpan.FromMinutes(model.InvoiceExpiration);
        blob.RefundBOLT11Expiration = TimeSpan.FromDays(model.BOLT11Expiration);
        if (!string.IsNullOrEmpty(model.BrandColor) && !ColorPalette.IsValid(model.BrandColor))
        {
            ModelState.AddModelError(nameof(model.BrandColor), "Invalid color");
            return View(model);
        }
        blob.BrandColor = model.BrandColor;

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
                    // delete existing file
                    if (!string.IsNullOrEmpty(blob.LogoFileId))
                    {
                        await fileService.RemoveFile(blob.LogoFileId, userId);
                    }
                    // add new image
                    try
                    {
                        var storedFile = await fileService.AddFile(model.LogoFile, userId);
                        blob.LogoFileId = storedFile.Id;
                    }
                    catch (Exception e)
                    {
                        ModelState.AddModelError(nameof(model.LogoFile), $"Could not save logo: {e.Message}");
                    }
                }
            }
        }
        else if (RemoveLogoFile && !string.IsNullOrEmpty(blob.LogoFileId))
        {
            await fileService.RemoveFile(blob.LogoFileId, userId);
            blob.LogoFileId = null;
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
                // delete existing file
                if (!string.IsNullOrEmpty(blob.CssFileId))
                {
                    await fileService.RemoveFile(blob.CssFileId, userId);
                }
                // add new file
                try
                {
                    var storedFile = await fileService.AddFile(model.CssFile, userId);
                    blob.CssFileId = storedFile.Id;
                }
                catch (Exception e)
                {
                    ModelState.AddModelError(nameof(model.CssFile), $"Could not save CSS file: {e.Message}");
                }
            }
        }
        else if (RemoveCssFile && !string.IsNullOrEmpty(blob.CssFileId))
        {
            await fileService.RemoveFile(blob.CssFileId, userId);
            blob.CssFileId = null;
            needUpdate = true;
        }

        if (CurrentStore.SetStoreBlob(blob))
        {
            needUpdate = true;
        }

        if (needUpdate)
        {
            await repo.UpdateStore(CurrentStore);

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
        await repo.UpdateStore(CurrentStore);

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
        await repo.DeleteStore(CurrentStore.Id);
        TempData[WellKnownTempData.SuccessMessage] = "Store successfully deleted.";
        return RedirectToAction(nameof(UIHomeController.Index), "UIHome");
    }

    [HttpGet("{storeId}/checkout")]
    public IActionResult CheckoutAppearance()
    {
        var storeBlob = CurrentStore.GetStoreBlob();
        var vm = new CheckoutAppearanceViewModel();
        SetCryptoCurrencies(vm, CurrentStore);
        vm.PaymentMethodCriteria = CurrentStore.GetSupportedPaymentMethods(networkProvider)
            .Where(s => !storeBlob.GetExcludedPaymentMethods().Match(s.PaymentId))
            .Where(s => networkProvider.GetNetwork(s.PaymentId.CryptoCode) != null)
            .Where(s => s.PaymentId.PaymentType != PaymentTypes.LNURLPay)
            .Select(method =>
            {
                var existing = storeBlob.PaymentMethodCriteria.SingleOrDefault(criteria =>
                    criteria.PaymentMethod == method.PaymentId);
                return existing is null
                    ? new PaymentMethodCriteriaViewModel { PaymentMethod = method.PaymentId.ToString(), Value = "" }
                    : new PaymentMethodCriteriaViewModel
                    {
                        PaymentMethod = existing.PaymentMethod.ToString(),
                        Type = existing.Above
                            ? PaymentMethodCriteriaViewModel.CriteriaType.GreaterThan
                            : PaymentMethodCriteriaViewModel.CriteriaType.LessThan,
                        Value = existing.Value?.ToString() ?? ""
                    };
            }).ToList();

        vm.UseClassicCheckout = storeBlob.CheckoutType == Client.Models.CheckoutType.V1;
        vm.CelebratePayment = storeBlob.CelebratePayment;
        vm.PlaySoundOnPayment = storeBlob.PlaySoundOnPayment;
        vm.OnChainWithLnInvoiceFallback = storeBlob.OnChainWithLnInvoiceFallback;
        vm.ShowPayInWalletButton = storeBlob.ShowPayInWalletButton;
        vm.ShowStoreHeader = storeBlob.ShowStoreHeader;
        vm.LightningAmountInSatoshi = storeBlob.LightningAmountInSatoshi;
        vm.RequiresRefundEmail = storeBlob.RequiresRefundEmail;
        vm.LazyPaymentMethods = storeBlob.LazyPaymentMethods;
        vm.RedirectAutomatically = storeBlob.RedirectAutomatically;
        vm.CustomCSS = storeBlob.CustomCSS;
        vm.CustomLogo = storeBlob.CustomLogo;
        vm.SoundFileId = storeBlob.SoundFileId;
        vm.HtmlTitle = storeBlob.HtmlTitle;
        vm.SupportUrl = storeBlob.StoreSupportUrl;
        vm.DisplayExpirationTimer = (int)storeBlob.DisplayExpirationTimer.TotalMinutes;
        vm.ReceiptOptions = CheckoutAppearanceViewModel.ReceiptOptionsViewModel.Create(storeBlob.ReceiptOptions);
        vm.AutoDetectLanguage = storeBlob.AutoDetectLanguage;
        vm.SetLanguages(langService, storeBlob.DefaultLang);

        return View(vm);
    }

    [HttpPost("{storeId}/checkout")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> CheckoutAppearance(CheckoutAppearanceViewModel model, [FromForm] bool removeSoundFile = false)
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
        model.SetLanguages(langService, model.DefaultLang);
        model.PaymentMethodCriteria ??= [];
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
                    // delete existing file
                    if (!string.IsNullOrEmpty(blob.SoundFileId))
                    {
                        await fileService.RemoveFile(blob.SoundFileId, userId);
                    }

                    // add new file
                    try
                    {
                        var storedFile = await fileService.AddFile(model.SoundFile, userId);
                        blob.SoundFileId = storedFile.Id;
                        needUpdate = true;
                    }
                    catch (Exception e)
                    {
                        ModelState.AddModelError(nameof(model.SoundFile), $"Could not save sound: {e.Message}");
                    }
                }
            }
        }
        else if (removeSoundFile && !string.IsNullOrEmpty(blob.SoundFileId))
        {
            await fileService.RemoveFile(blob.SoundFileId, userId);
            blob.SoundFileId = null;
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
            if (paymentMethodId.PaymentType == PaymentTypes.LightningLike)
                model.PaymentMethodCriteria.Add(new PaymentMethodCriteriaViewModel()
                {
                    PaymentMethod = new PaymentMethodId(paymentMethodId.CryptoCode, PaymentTypes.LNURLPay).ToString(),
                    Type = newCriteria.Type,
                    Value = newCriteria.Value
                });
            // Should not be able to set LNUrlPay criteria directly in UI
            if (paymentMethodId.PaymentType == PaymentTypes.LNURLPay)
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
        blob.CheckoutType = model.UseClassicCheckout ? Client.Models.CheckoutType.V1 : Client.Models.CheckoutType.V2;
        blob.CelebratePayment = model.CelebratePayment;
        blob.PlaySoundOnPayment = model.PlaySoundOnPayment;
        blob.OnChainWithLnInvoiceFallback = model.OnChainWithLnInvoiceFallback;
        blob.LightningAmountInSatoshi = model.LightningAmountInSatoshi;
        blob.RequiresRefundEmail = model.RequiresRefundEmail;
        blob.LazyPaymentMethods = model.LazyPaymentMethods;
        blob.RedirectAutomatically = model.RedirectAutomatically;
        blob.ReceiptOptions = model.ReceiptOptions.ToDTO();
        blob.CustomLogo = model.CustomLogo;
        blob.CustomCSS = model.CustomCSS;
        blob.HtmlTitle = string.IsNullOrWhiteSpace(model.HtmlTitle) ? null : model.HtmlTitle;
        blob.StoreSupportUrl = string.IsNullOrWhiteSpace(model.SupportUrl) ? null : model.SupportUrl.IsValidEmail() ? $"mailto:{model.SupportUrl}" : model.SupportUrl;
        blob.DisplayExpirationTimer = TimeSpan.FromMinutes(model.DisplayExpirationTimer);
        blob.AutoDetectLanguage = model.AutoDetectLanguage;
        blob.DefaultLang = model.DefaultLang;
        blob.NormalizeToRelativeLinks(Request);
        if (CurrentStore.SetStoreBlob(blob))
        {
            needUpdate = true;
        }
        if (needUpdate)
        {
            await repo.UpdateStore(CurrentStore);
            TempData[WellKnownTempData.SuccessMessage] = "Store successfully updated";
        }

        return RedirectToAction(nameof(CheckoutAppearance), new
        {
            storeId = CurrentStore.Id
        });
    }

    private void SetCryptoCurrencies(CheckoutAppearanceViewModel vm, StoreData storeData)
    {
        var choices = GetEnabledPaymentMethodChoices(storeData);
        var chosen = GetDefaultPaymentMethodChoice(storeData);

        vm.PaymentMethods = new SelectList(choices, nameof(chosen.Value), nameof(chosen.Name), chosen?.Value);
        vm.DefaultPaymentMethod = chosen?.Value;
    }

    private PaymentMethodOptionViewModel.Format? GetDefaultPaymentMethodChoice(StoreData storeData)
    {
        var enabled = storeData.GetEnabledPaymentIds(networkProvider);
        var defaultPaymentId = storeData.GetDefaultPaymentId();
        var defaultChoice = defaultPaymentId?.FindNearest(enabled);
        if (defaultChoice is null)
        {
            defaultChoice = enabled.FirstOrDefault(e => e.CryptoCode == networkProvider.DefaultNetwork.CryptoCode && e.PaymentType == PaymentTypes.BTCLike) ??
                            enabled.FirstOrDefault(e => e.CryptoCode == networkProvider.DefaultNetwork.CryptoCode && e.PaymentType == PaymentTypes.LightningLike) ??
                            enabled.FirstOrDefault();
        }
        var choices = GetEnabledPaymentMethodChoices(storeData);

        return defaultChoice is null ? null : choices.FirstOrDefault(c => defaultChoice.ToString().Equals(c.Value, StringComparison.OrdinalIgnoreCase));
    }
}
