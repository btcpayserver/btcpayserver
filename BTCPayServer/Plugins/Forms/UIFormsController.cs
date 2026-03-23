#nullable enable
using System;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Form;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Controllers;
using BTCPayServer.Data;
using BTCPayServer.Filters;
using BTCPayServer.Forms.Models;
using BTCPayServer.Models;
using BTCPayServer.Plugins.Forms;
using BTCPayServer.Services;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace BTCPayServer.Forms;

[Area(FormsPlugin.Area)]
[Authorize(Policy = Policies.CanViewStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
public class UIFormsController(
    FormDataService formDataService,
    UIInvoiceController invoiceController,
    UriResolver uriResolver,
    IStringLocalizer stringLocalizer,
    StoreRepository storeRepository,
    IAuthorizationService authorizationService)
    : Controller
{
    private IStringLocalizer StringLocalizer { get; } = stringLocalizer;

    [HttpGet("~/stores/{storeId}/forms")]
    public async Task<IActionResult> FormsList(string storeId)
    {
        var forms = await formDataService.GetForms(storeId);

        return View(forms);
    }

    [HttpGet("~/stores/{storeId}/forms/new")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public IActionResult Create(string storeId)
    {
        var vm = new ModifyForm { FormConfig = new Form().ToString() };
        return View("Modify", vm);
    }

    [HttpGet("~/stores/{storeId}/forms/modify/{id}")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> Modify(string storeId, string id)
    {
        var form = await formDataService.GetForm(storeId, id);
        if (form is null)
            return NotFound();

        var config = Form.Parse(form.Config);
        return View(new ModifyForm { Name = form.Name, FormConfig = config.ToString(), Public = form.Public });
    }

    [HttpPost("~/stores/{storeId}/forms/modify/{id?}")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> Modify(string storeId, string? id, ModifyForm modifyForm)
    {
        if (id is not null)
        {
            if (await formDataService.GetForm(storeId, id) is null)
            {
                return NotFound();
            }
        }

        if (!formDataService.IsFormSchemaValid(modifyForm.FormConfig, out var form, out var error))
        {
            ModelState.AddModelError(nameof(modifyForm.FormConfig), StringLocalizer["Form config was invalid: {0}", error!]);
        }
        else
        {
            modifyForm.FormConfig = form.ToString();
        }

        if (!ModelState.IsValid)
        {
            return View(modifyForm);
        }

        try
        {
            var formData = new FormData
            {
                Id = id,
                StoreId = storeId,
                Name = modifyForm.Name,
                Config = modifyForm.FormConfig,
                Public = modifyForm.Public
            };
            var isNew = id is null;
            await formDataService.AddOrUpdateForm(formData);
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Severity = StatusMessageModel.StatusSeverity.Success,
                Message = isNew
                    ? StringLocalizer["Form created successfully."].Value
                    : StringLocalizer["Form updated successfully."].Value
            });
            if (isNew)
            {
                return RedirectToAction("Modify", new { storeId, id = formData.Id });
            }
        }
        catch (Exception e)
        {
            ModelState.AddModelError("", StringLocalizer["An error occurred while saving: {0}", e.Message]);
        }

        return View(modifyForm);
    }

    [HttpPost("~/stores/{storeId}/forms/{id}/remove")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> Remove(string storeId, string id)
    {
        await formDataService.RemoveForm(id, storeId);
        TempData.SetStatusMessageModel(new StatusMessageModel
        {
            Severity = StatusMessageModel.StatusSeverity.Success,
            Message = StringLocalizer["Form removed"].Value
        });
        return RedirectToAction("FormsList", new { storeId });
    }

    [AllowAnonymous]
    [HttpGet("~/forms/{formId}")]
    [XFrameOptions(XFrameOptionsAttribute.XFrameOptions.Unset)]
    public async Task<IActionResult> ViewPublicForm(string? formId)
    {
        FormData? formData = await formDataService.GetForm(formId);
        if (formData?.Config is null)
        {
            return NotFound();
        }

        if (!formData.Public &&
            !(await authorizationService.AuthorizeAsync(User, formData.StoreId, Policies.CanViewStoreSettings)).Succeeded)
        {
            return NotFound();
        }

        return await GetFormView(formData);
    }

    async Task<ViewResult> GetFormView(FormData formData, Form? form = null)
    {
        form ??= Form.Parse(formData.Config);
        form.ApplyValuesFromForm(Request.Query);
        var store = formData.Store ?? await storeRepository.FindStore(formData.StoreId);
        var storeBlob = store?.GetStoreBlob();

        return View("View", new FormViewModel
        {
            FormName = formData.Name,
            Form = form,
            StoreName = store?.StoreName,
            StoreBranding = await StoreBrandingViewModel.CreateAsync(Request, uriResolver, storeBlob)
        });
    }

    [AllowAnonymous]
    [HttpPost("~/forms/{formId}")]
    [XFrameOptions(XFrameOptionsAttribute.XFrameOptions.Unset)]
    public async Task<IActionResult> SubmitForm(string formId)
    {
        var formData = await formDataService.GetForm(formId);
        if (formData?.Config is null)
        {
            return NotFound();
        }

        if (!formData.Public &&
            !(await authorizationService.AuthorizeAsync(User, formData.StoreId, Policies.CanViewStoreSettings)).Succeeded)
        {
            return NotFound();
        }

        if (!Request.HasFormContentType)
            return await GetFormView(formData);

        var form = Form.Parse(formData.Config);
        form.ApplyValuesFromForm(Request.Form);

        if (!formDataService.Validate(form, ModelState))
            return await GetFormView(formData, form);

        // Create invoice after public form has been filled
        var store = await storeRepository.FindStore(formData.StoreId);
        if (store is null)
            return NotFound();

        try
        {
            var request = formDataService.GenerateInvoiceParametersFromForm(form);
            var inv = await invoiceController.CreateInvoiceCoreRaw(request, store, Request.GetAbsoluteRoot());
            if (inv.Price == 0 && inv.Type == InvoiceType.Standard && inv.ReceiptOptions?.Enabled is not false)
            {
                return RedirectToAction("InvoiceReceipt", "UIInvoice", new { invoiceId = inv.Id });
            }
            return RedirectToAction("Checkout", "UIInvoice", new { invoiceId = inv.Id });
        }
        catch (Exception e)
        {
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Severity = StatusMessageModel.StatusSeverity.Error,
                Message = StringLocalizer["Could not generate invoice: {0}", e.Message].Value
            });
            return await GetFormView(formData, form);
        }
    }
}
