#nullable enable
using System;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Form;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Controllers;
using BTCPayServer.Data.Data;
using BTCPayServer.Forms.Models;
using BTCPayServer.Models;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Forms;

[Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
public class UIFormsController : Controller
{
    private readonly FormDataService _formDataService;
    private FormComponentProviders FormProviders { get; }

    public UIFormsController(FormComponentProviders formProviders, FormDataService formDataService)
    {
        FormProviders = formProviders;
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
        var vm = new ModifyForm { FormConfig = new Form().ToString() };
        return View("Modify", vm);
    }

    [HttpGet("~/stores/{storeId}/forms/modify/{id}")]
    public async Task<IActionResult> Modify(string storeId, string id)
    {
        var form = await _formDataService.GetForm(storeId, id);
        if (form is null) return NotFound();
        
        var config = Form.Parse(form.Config);
        return View(new ModifyForm { Name = form.Name, FormConfig = config.ToString() });
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
            modifyForm.FormConfig = Form.Parse(modifyForm.FormConfig).ToString();
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
        if (!IsValidRedirectUri(redirectUrl))
            return BadRequest();
        
        FormData? formData = string.IsNullOrEmpty(formId) ? null : await _formDataService.GetForm(formId);
        if (formData == null)
        {
            return string.IsNullOrEmpty(redirectUrl)
                ? NotFound()
                : Redirect(redirectUrl);
        }
        
        return GetFormView(formData, redirectUrl);
    }

    ViewResult GetFormView(FormData formData, string? redirectUrl)
    {
        return View("View", new FormViewModel { FormData = formData, RedirectUrl = redirectUrl });
    }
    [AllowAnonymous]
    [HttpPost("~/forms/{formId}")]
    public async Task<IActionResult> SubmitForm(string formId, string? redirectUrl, string? command)
    {
        if (!IsValidRedirectUri(redirectUrl))
            return BadRequest();
        
        var formData = await _formDataService.GetForm(formId);
        if (formData?.Config is null)
            return NotFound();
        
        if (command is not "Submit")
            return GetFormView(formData, redirectUrl);

        var conf = Form.Parse(formData.Config);
        conf.ApplyValuesFromForm(Request.Form);
        if (!FormProviders.Validate(conf, ModelState))
            return GetFormView(formData, redirectUrl);

        var form = new MultiValueDictionary<string, string>();
        foreach (var kv in Request.Form)
            form.Add(kv.Key, kv.Value);
        
        // With redirect, the form comes from another entity that we need to send the data back to
        if (!string.IsNullOrEmpty(redirectUrl))
        {
            return View("PostRedirect", new PostRedirectViewModel
            {
                FormUrl = redirectUrl,
                FormParameters = form
            });
        }

        // Create invoice after public form has been filled
        //var store = await storeRepository.FindStore(formData.StoreId);
        //if (store is null)
        //    return NotFound();

        //var amt = dbForm.GetFieldByName("internal_amount")?.Value;
        //var request = new CreateInvoiceRequest
        //{
        //    Currency = dbForm.GetFieldByName("internal_currency")?.Value ?? store.GetStoreBlob().DefaultCurrency,
        //    Amount = string.IsNullOrEmpty(amt) ? null : int.Parse(amt, CultureInfo.InvariantCulture),
        //    Metadata = JObject.FromObject(data)
        //};
        //var inv = await invoiceController.CreateInvoiceCoreRaw(request, store, Request.GetAbsoluteRoot());

        //return RedirectToAction("Checkout", "UIInvoice", new { invoiceId = inv.Id });
        return NotFound();
    }
    
    private bool IsValidRedirectUri(string? redirectUrl) =>
        !string.IsNullOrEmpty(redirectUrl) && Uri.TryCreate(redirectUrl, UriKind.RelativeOrAbsolute, out var uri) &&
        (Url.IsLocalUrl(redirectUrl) || uri.Host.Equals(Request.Host.Host));
}
