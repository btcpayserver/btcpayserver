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
using BTCPayServer.Models;
using BTCPayServer.Services;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json;
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
        return new[] {
            "text",
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
    private readonly IMemoryCache _memoryCache;

    public UIFormsController(FormDataService formDataService, IMemoryCache memoryCache)
    {
        _formDataService = formDataService;
        _memoryCache = memoryCache;
    }

    [HttpGet("~/stores/{storeId}/forms")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> FormsList(string storeId)
    {
        var forms = await _formDataService.GetForms(new FormDataService.FormQuery {Stores = new[] {storeId}});

        return View(forms);
    }

    [HttpGet("~/stores/{storeId}/forms/new")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public IActionResult Create(string storeId)
    {
        var vm = new ModifyForm { FormConfig = JObject.FromObject(new Form()).ToString() };
        return View("Modify", vm);
    }

    [HttpGet("~/stores/{storeId}/forms/modify/{id}")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> Modify(string storeId, string id)
    {
        var query = new FormDataService.FormQuery { Stores = new[] { storeId }, Ids = new[] { id } };
        var form = (await _formDataService.GetForms(query)).FirstOrDefault();
        if (form is null) return NotFound();
        
        var json = JsonConvert.DeserializeObject(form.Config);
        var config = JsonConvert.SerializeObject(json, Formatting.Indented);
        return View(new ModifyForm { Name = form.Name, FormConfig = config });
    }

    [HttpPost("~/stores/{storeId}/forms/modify/{id?}")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    public async Task<IActionResult> Modify(string storeId, string id, ModifyForm modifyForm)
    {
        if (id is not null)
        {
            var query = new FormDataService.FormQuery { Stores = new[] { storeId }, Ids = new[] { id } };
            var form = (await _formDataService.GetForms(query)).FirstOrDefault();
            if (form is null)
            {
                return NotFound();
            }
        }

        try
        {
            modifyForm.FormConfig = JObject.FromObject(JObject.Parse(modifyForm.FormConfig).ToObject<Form>()).ToString();
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
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
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
    [HttpGet("~/forms/{id}")]
    public async Task<IActionResult> ViewForm(string id, string redirectUrl)
    {
        TempData["redirectUrl"] = redirectUrl;
        FormData form = await GetFormData(id);

        return form is null
            ? NotFound()
            : View("View", form);
    }

    private async Task<FormData> GetFormData(string id)
    {
        FormData form;
        switch (id)
        {
            case { } formid when formid == GenericFormOption.Address.ToString():
                form = new FormData()
                {
                    Config = JObject.FromObject(FormDataService.StaticFormAddress).ToString(),
                    Id = GenericFormOption.Address.ToString(),
                    Name = "Address Form",
                };
                break;

            case { } formid when formid == GenericFormOption.Email.ToString():
                form = new FormData()
                {
                    Config = JObject.FromObject(FormDataService.StaticFormEmail).ToString(),
                    Id = GenericFormOption.Email.ToString(),
                    Name = "Email Form",
                };

                break;
            default:
                var query = new FormDataService.FormQuery {Ids = new[] {id}};
                form = (await _formDataService.GetForms(query)).FirstOrDefault();

                break;
        }

        // if (form is not null && redirectUrl is not null)
        // {
        //     var f = form.Deserialize().ToObject<Form>();
        //     f.Fields.Add(new HtmlInputField(null, "integration_redirectUrl", redirectUrl, true, null, "hidden"));
        //     form.Config = JObject.FromObject(f).Serialize();
        // }
        return form;
    }

    [AllowAnonymous]
    [HttpPost("~/forms/{id}")]
    public async Task<IActionResult> SubmitForm(
        string id,
        [FromServices]StoreRepository storeRepository,  
        [FromServices] UIInvoiceController invoiceController)
    {
        var orig = await GetFormData(id);
        if (orig is null)
        {
            return NotFound();
        }

        var dbForm = JObject.Parse(orig.Config).ToObject<Form>();
        dbForm.ApplyValuesFromForm(Request.Form, "internal");

        Dictionary<string, object> data = dbForm.GetValues();
        data.TryAdd("formResponse", orig.Id);
        
        // var redirect = dbForm.GetFieldByName("integration_redirectUrl")?.Value;
        if (TempData.TryGetValue("redirectUrl", out var r) && r is string redirect)
        {
            TempData["formResponse"] = JsonConvert.SerializeObject(data);
            
            return View("PostRedirect", new PostRedirectViewModel
            {
                FormUrl = redirect
            });

        }
        
        var store = await storeRepository.FindStore(orig.StoreId);
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
}
