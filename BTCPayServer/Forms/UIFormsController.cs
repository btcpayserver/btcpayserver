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
using BTCPayServer.Services;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Forms;

public interface IFormComponentProvider
{
    public string CanHandle(Field field);
    
}

public class HtmlInputFormProvider: IFormComponentProvider
{
    public string CanHandle(Field field)
    {
    
           
        return new[] { "text",
            "radio",
            "checkbox",
            "password",
            "file",
            "hidden",
            "button",
            "submit",
            "color",
            "date",
            "datetime-local",
            "month",
            "week",
            "time",
            "email",
            "image",
            "number",
            "range",
            "search",
            "url",
            "tel",
            "reset"}.Contains(field.Type) ? "Forms/InputElement" : null;
    }
}
public class HtmlFieldsetFormProvider: IFormComponentProvider
{
    public string CanHandle(Field field)
    {
    
           
        return new[] { "fieldset"}.Contains(field.Type) ? "Forms/FieldSetElement" : null;
    }
}

public class FormComponentProvider : IFormComponentProvider
{
    private readonly IEnumerable<IFormComponentProvider> _formComponentProviders;

    public FormComponentProvider(IEnumerable<IFormComponentProvider> formComponentProviders)
    {
        _formComponentProviders = formComponentProviders;
    }
    public string CanHandle(Field field)
    {
        return _formComponentProviders.Select(formComponentProvider => formComponentProvider.CanHandle(field)).FirstOrDefault(result => !string.IsNullOrEmpty(result));
    }
}

public class UIFormsController : Controller
{
    private readonly FormDataService _formDataService;

    public UIFormsController(
        EventAggregator eventAggregator,
        BTCPayNetworkProvider btcPayNetworkProvider,
        FormDataService formDataService,
        StoreRepository storeRepository,
        BTCPayNetworkJsonSerializerSettings btcPayNetworkJsonSerializerSettings)
    {
        _formDataService = formDataService;
    }

    [HttpGet("~/stores/{storeId}/forms")]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> FormsList(string storeId)
    {
        var forms =
            (await _formDataService.GetForms(
                new FormDataService.FormQuery() {Stores = new[] {storeId}}));

        return View(forms);
    }

    [HttpGet("~/stores/{storeId}/forms/new")]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> Create(string storeId)
    {
        return View("Modify", new ModifyForm() {FormConfig = JObject.FromObject(new Form()).ToString()});
    }

    [HttpGet("~/stores/{storeId}/forms/modify/{id}")]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> Modify(string storeId, string id)
    {
        var form =
            (await _formDataService.GetForms(
                new FormDataService.FormQuery() {Stores = new[] {storeId}, Ids = new[] {id}})).FirstOrDefault();
        if (form is null)
        {
            return NotFound();
        }

        return View(new ModifyForm() {Name = form.Name, FormConfig = form.Config});
    }

    [AllowAnonymous]
    [HttpGet("~/forms/{id}")]
    public async Task<IActionResult> ViewForm(string id)
    {
        var form =
            (await _formDataService.GetForms(
                new FormDataService.FormQuery() { Ids = new[] {id}})).FirstOrDefault();
        if (form is null)
        {
            return NotFound();
        }

        return View("View", form);
    }

    [AllowAnonymous]
    [HttpPost("~/forms/{id}")]
    public async Task<IActionResult> SubmitForm(
        string id,
        [FromServices]StoreRepository storeRepository,  
        [FromServices] UIInvoiceController invoiceController)
    {
        var orig =
            (await _formDataService.GetForms(
                new FormDataService.FormQuery() { Ids = new[] {id}})).FirstOrDefault();
        if (orig is null)
        {
            return NotFound();
        }

        var dbForm = JObject.Parse(orig.Config).ToObject<Form>();
        dbForm.ApplyValuesFromForm(Request.Form);

        Dictionary<string, object> data = dbForm.GetValues();

        var store = await storeRepository.FindStore(orig.StoreId);
        var amt = dbForm.GetFieldByName("internal_amount")?.Value;
        
        var inv = await invoiceController.CreateInvoiceCoreRaw(
            new CreateInvoiceRequest()
            {
                Currency = dbForm.GetFieldByName("internal_currency")?.Value ??store.GetStoreBlob().DefaultCurrency, 
                Amount = string.IsNullOrEmpty(amt)? null: int.Parse(amt, CultureInfo.InvariantCulture),
                
                Metadata = JObject.FromObject(data)
            }, store, Request.GetAbsoluteRoot());

        return RedirectToAction("Checkout", "UIInvoice", new {invoiceId = inv.Id});
    }

    [HttpPost("~/stores/{storeId}/forms/modify/{id?}")]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> Modify(string storeId, string? id, ModifyForm modifyForm)
    {
        if (id is not null)
        {
            var form =
                (await _formDataService.GetForms(
                    new FormDataService.FormQuery() {Stores = new[] {storeId}, Ids = new[] {id}})).FirstOrDefault();
            if (form is null)
            {
                return NotFound();
            }
        }

        try
        {
            modifyForm.FormConfig =
                JObject.FromObject(JObject.Parse(modifyForm.FormConfig).ToObject<Form>()).ToString();
        }
        catch (Exception e)
        {
            ModelState.AddModelError(nameof(modifyForm.FormConfig), "Form config was invalid");
        }

        if (!ModelState.IsValid)
        {
            return View(modifyForm);
        }

        try
        {
            var form = new FormData() {StoreId = storeId, Name = modifyForm.Name, Config = modifyForm.FormConfig, Id = id};
            await _formDataService.AddOrUpdateForm(form);
            TempData.SetStatusMessageModel(new StatusMessageModel() {Message = $"Form {(id is null? "created": "updated")} successfully."});
            if (id is null)
            {
                return RedirectToAction("Modify", new {storeId = storeId, id = form.Id});
            }
        }
        catch (Exception e)
        {
            ModelState.AddModelError("", $"An error occurred while saving: {e.Message}");
        }

        return View(modifyForm);
    }


    [HttpPost("~/stores/{storeId}/forms/{id}/remove")]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> Remove(string storeId, string id)
    {
        await _formDataService.RemoveForm(id, storeId);
        TempData.SetStatusMessageModel(new StatusMessageModel()
        {
            Severity = StatusMessageModel.StatusSeverity.Success, Message = "Form removed"
        });
        return RedirectToAction("FormsList", new {storeId});
    }
}
