#nullable enable
using System;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Form;
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
    private FormComponentProviders FormProviders { get; }

    public UIFormsController(FormComponentProviders formProviders)
    {
        FormProviders = formProviders;
    }

    [AllowAnonymous]
    [HttpGet("~/forms/{formId}")]
    [HttpPost("~/forms")]
    public IActionResult ViewPublicForm(string? formId, string? redirectUrl)
    {
        if (!IsValidRedirectUri(redirectUrl))
            return BadRequest();
        
        FormData? formData = string.IsNullOrEmpty(formId) ? null : GetFormData(formId);
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
    public IActionResult SubmitForm(string formId, string? redirectUrl, string? command)
    {
        if (!IsValidRedirectUri(redirectUrl))
            return BadRequest();
        
        var formData = GetFormData(formId);
        if (formData?.Config is null)
            return NotFound();
        
        if (!Request.HasFormContentType)
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

        return NotFound();
    }

    internal static FormData? GetFormData(string id)
    {
        FormData? form = id switch
        {
            { } formId when formId == GenericFormOption.Address.ToString() => new FormData
            {
                Config = FormDataService.StaticFormAddress.ToString(),
                Id = GenericFormOption.Address.ToString(),
                Name = "Provide your address",
            },
            { } formId when formId == GenericFormOption.Email.ToString() => new FormData
            {
                Config = FormDataService.StaticFormEmail.ToString(),
                Id = GenericFormOption.Email.ToString(),
                Name = "Provide your email address",
            },
            _ => null
        };
        return form;
    }
    
    private bool IsValidRedirectUri(string? redirectUrl) =>
        !string.IsNullOrEmpty(redirectUrl) && Uri.TryCreate(redirectUrl, UriKind.RelativeOrAbsolute, out var uri) &&
        (Url.IsLocalUrl(redirectUrl) || uri.Host.Equals(Request.Host.Host));
}
