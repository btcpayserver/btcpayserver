#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Form;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Controllers;
using BTCPayServer.Data;
using BTCPayServer.Data.Data;
using BTCPayServer.Forms.Models;
using BTCPayServer.Models;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Forms;

[Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
public class UIFormsController : Controller
{
    private readonly FormDataService _formDataService;

    public UIFormsController(FormDataService formDataService)
    {
        _formDataService = formDataService;
    }

    [HttpGet("~/stores/{storeId}/forms")]
    public async Task<IActionResult> FormsList(string storeId)
    {
        var forms = await _formDataService.GetForms(storeId);

        return View(forms);
    }

    [HttpGet("~/stores/{storeId}/forms/new")]
    public IActionResult Create(string storeId)
    {
        var vm = new ModifyForm { FormConfig = JObject.FromObject(new Form()).ToString() };
        return View("Modify", vm);
    }

    [HttpGet("~/stores/{storeId}/forms/modify/{id}")]
    public async Task<IActionResult> Modify(string storeId, string id)
    {
        var form = await _formDataService.GetForm(storeId, id);
        if (form is null) return NotFound();
        
        var json = JsonConvert.DeserializeObject(form.Config);
        var config = JsonConvert.SerializeObject(json, Formatting.Indented);
        return View(new ModifyForm { Name = form.Name, FormConfig = config });
    }

    [HttpPost("~/stores/{storeId}/forms/modify/{id?}")]
    public async Task<IActionResult> Modify(string storeId, string? id, ModifyForm modifyForm)
    {
        if (id is not null)
        {
            var form = await _formDataService.GetForm(storeId, id);
            if (form is null)
            {
                return NotFound();
            }
        }

        try
        {
            modifyForm.FormConfig = JObject.FromObject(JObject.Parse(modifyForm.FormConfig).ToObject<Form>()!).ToString();
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(nameof(modifyForm.FormConfig), $"Form config was invalid: {ex.Message}");
        }

        if (!ModelState.IsValid)
        {
            return View(modifyForm);
        }

        try
        {
            var form = new FormData
            {
                Id = id,
                StoreId = storeId,
                Name = modifyForm.Name,
                Config = modifyForm.FormConfig
            };
            var isNew = id is null;
            await _formDataService.AddOrUpdateForm(form);
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Severity = StatusMessageModel.StatusSeverity.Success,
                Message = $"Form {(isNew ? "created": "updated")} successfully."
            });
            if (isNew)
            {
                return RedirectToAction("Modify", new { storeId, id = form.Id });
            }
        }
        catch (Exception e)
        {
            ModelState.AddModelError("", $"An error occurred while saving: {e.Message}");
        }

        return View(modifyForm);
    }

    [HttpPost("~/stores/{storeId}/forms/{id}/remove")]
    public async Task<IActionResult> Remove(string storeId, string id)
    {
        await _formDataService.RemoveForm(id, storeId);
        TempData.SetStatusMessageModel(new StatusMessageModel
        {
            Severity = StatusMessageModel.StatusSeverity.Success,
            Message = "Form removed"
        });
        return RedirectToAction("FormsList", new { storeId });
    }
    
    [AllowAnonymous]
    [HttpGet("~/forms/{formId}")]
    [HttpPost("~/forms")]
    public async Task<IActionResult> ViewPublicForm(string? formId, string? redirectUrl)
    {
        FormData? formData = string.IsNullOrEmpty(formId) ? null : await GetFormData(formId);
        if (formData == null)
        {
            return string.IsNullOrEmpty(redirectUrl)
                ? NotFound()
                : Redirect(redirectUrl);
        }
        
        return View("View", new FormViewModel { FormData = formData, RedirectUrl = redirectUrl });
    }

    [AllowAnonymous]
    [HttpPost("~/forms/{formId}")]
    public async Task<IActionResult> SubmitForm(
        string formId, string? redirectUrl,
        [FromServices] StoreRepository storeRepository,  
        [FromServices] UIInvoiceController invoiceController)
    {
        var formData = await GetFormData(formId);
        if (formData is null)
        {
            return NotFound();
        }

        var dbForm = JObject.Parse(formData.Config).ToObject<Form>()!;
        dbForm.ApplyValuesFromForm(Request.Form, "internal");
        Dictionary<string, object> data = dbForm.GetValues();
        
        // With redirect, the form comes from another entity that we need to send the data back to
        if (!string.IsNullOrEmpty(redirectUrl))
        {
            return View("PostRedirect", new PostRedirectViewModel
            {
                FormUrl = redirectUrl,
                FormParameters =
                {
                    { "formId", formData.Id },
                    { "formData", JsonConvert.SerializeObject(data) }
                }
            });
        }
        
        // Create invoice after public form has been filled
        var store = await storeRepository.FindStore(formData.StoreId);
        if (store is null)
            return NotFound();
        
        var amt = dbForm.GetFieldByName("internal_amount")?.Value;
        var request = new CreateInvoiceRequest
        {
            Currency = dbForm.GetFieldByName("internal_currency")?.Value ?? store.GetStoreBlob().DefaultCurrency,
            Amount = string.IsNullOrEmpty(amt) ? null : int.Parse(amt, CultureInfo.InvariantCulture),
            Metadata = JObject.FromObject(data)
        };
        var inv = await invoiceController.CreateInvoiceCoreRaw(request, store, Request.GetAbsoluteRoot());

        return RedirectToAction("Checkout", "UIInvoice", new { invoiceId = inv.Id });
    }

    private async Task<FormData?> GetFormData(string id)
    {
        FormData? form = id switch
        {
            { } formId when formId == GenericFormOption.Address.ToString() => new FormData
            {
                Config = JObject.FromObject(FormDataService.StaticFormAddress).ToString(),
                Id = GenericFormOption.Address.ToString(),
                Name = "Provide your address",
            },
            { } formId when formId == GenericFormOption.Email.ToString() => new FormData
            {
                Config = JObject.FromObject(FormDataService.StaticFormEmail).ToString(),
                Id = GenericFormOption.Email.ToString(),
                Name = "Provide your email address",
            },
            _ => await _formDataService.GetForm(id)
        };
        return form;
    }
}
