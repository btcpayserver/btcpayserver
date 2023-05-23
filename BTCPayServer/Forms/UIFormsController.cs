#nullable enable
using System;
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
using BTCPayServer.Filters;
using BTCPayServer.Forms.Models;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Forms;

[Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
public class UIFormsController : Controller
{
    private readonly FormDataService _formDataService;
    private readonly IAuthorizationService _authorizationService;
    private readonly StoreRepository _storeRepository;
    private FormComponentProviders FormProviders { get; }

    public UIFormsController(FormComponentProviders formProviders, FormDataService formDataService,
        StoreRepository storeRepository, IAuthorizationService authorizationService)
    {
        FormProviders = formProviders;
        _formDataService = formDataService;
        _authorizationService = authorizationService;
        _storeRepository = storeRepository;
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
        if (form is null)
            return NotFound();

        var config = Form.Parse(form.Config);
        return View(new ModifyForm { Name = form.Name, FormConfig = config.ToString(), Public = form.Public });
    }

    [HttpPost("~/stores/{storeId}/forms/modify/{id?}")]
    public async Task<IActionResult> Modify(string storeId, string? id, ModifyForm modifyForm)
    {
        if (id is not null)
        {
            if (await _formDataService.GetForm(storeId, id) is null)
            {
                return NotFound();
            }
        }

        if (!_formDataService.IsFormSchemaValid(modifyForm.FormConfig, out var form, out var error))
        {
            ModelState.AddModelError(nameof(modifyForm.FormConfig),
                $"Form config was invalid: {error})");
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
            await _formDataService.AddOrUpdateForm(formData);
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Severity = StatusMessageModel.StatusSeverity.Success,
                Message = $"Form {(isNew ? "created" : "updated")} successfully."
            });
            if (isNew)
            {
                return RedirectToAction("Modify", new { storeId, id = formData.Id });
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
    [XFrameOptions(XFrameOptionsAttribute.XFrameOptions.Unset)]
    public async Task<IActionResult> ViewPublicForm(string? formId)
    {
        FormData? formData = await _formDataService.GetForm(formId);
        if (formData?.Config is null)
        {
            return NotFound();
        }

        if (!formData.Public &&
            !(await _authorizationService.AuthorizeAsync(User, Policies.CanViewStoreSettings)).Succeeded)
        {
            return NotFound();
        }

        return await GetFormView(formData);
    }

    async Task<ViewResult> GetFormView(FormData formData, Form? form = null)
    {
        form ??= Form.Parse(formData.Config);
        form.ApplyValuesFromForm(Request.Query);
        var store = formData.Store ?? await _storeRepository.FindStore(formData.StoreId);
        var storeBlob = store?.GetStoreBlob();

        return View("View", new FormViewModel
        {
            FormName = formData.Name,
            Form = form,
            StoreName = store?.StoreName,
            BrandColor = storeBlob?.BrandColor,
            CssFileId = storeBlob?.CssFileId,
            LogoFileId = storeBlob?.LogoFileId,
        });
    }

    [AllowAnonymous]
    [HttpPost("~/forms/{formId}")]
    [XFrameOptions(XFrameOptionsAttribute.XFrameOptions.Unset)]
    public async Task<IActionResult> SubmitForm(string formId,
        [FromServices] StoreRepository storeRepository,
        [FromServices] UIInvoiceController invoiceController)
    {
        var formData = await _formDataService.GetForm(formId);
        if (formData?.Config is null)
        {
            return NotFound();
        }

        if (!formData.Public &&
            !(await _authorizationService.AuthorizeAsync(User, Policies.CanViewStoreSettings)).Succeeded)
        {
            return NotFound();
        }

        if (!Request.HasFormContentType)
            return await GetFormView(formData);

        var form = Form.Parse(formData.Config);
        form.ApplyValuesFromForm(Request.Form);

        if (!_formDataService.Validate(form, ModelState))
            return await GetFormView(formData, form);

        // Create invoice after public form has been filled
        var store = await storeRepository.FindStore(formData.StoreId);
        if (store is null)
            return NotFound();

        var request = _formDataService.GenerateInvoiceParametersFromForm(form);
        var inv = await invoiceController.CreateInvoiceCoreRaw(request, store, Request.GetAbsoluteRoot());

        return RedirectToAction("Checkout", "UIInvoice", new { invoiceId = inv.Id });
    }
}
