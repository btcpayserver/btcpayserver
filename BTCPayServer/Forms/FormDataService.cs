#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Form;
using BTCPayServer.Data;
using BTCPayServer.Data.Data;
using BTCPayServer.Models;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Forms;

public class FormDataService
{
    private readonly ApplicationDbContextFactory _applicationDbContextFactory;
    private readonly FormComponentProviders _formProviders;

    public FormDataService(
        ApplicationDbContextFactory applicationDbContextFactory, 
        FormComponentProviders formProviders)
    {
        _applicationDbContextFactory = applicationDbContextFactory;
        _formProviders = formProviders;
    }

    public static readonly Form StaticFormEmail = new()
    {
        Fields = new List<Field>() { Field.Create("Enter your email", "buyerEmail", null, true, null, "email") }
    };

    public static readonly Form StaticFormAddress = new()
    {
        Fields = new List<Field>()
        {
            Field.Create("Enter your email", "buyerEmail", null, true, null, "email"),
            Field.Create("Name", "buyerName", null, true, null),
            Field.Create("Address Line 1", "buyerAddress1", null, true, null),
            Field.Create("Address Line 2", "buyerAddress2", null, false, null),
            Field.Create("City", "buyerCity", null, true, null),
            Field.Create("Postcode", "buyerZip", null, false, null),
            Field.Create("State", "buyerState", null, false, null),
            Field.Create("Country", "buyerCountry", null, true, null)
        }
    };
    private static Dictionary<string, (string selectText, string name, Form form)> HardcodedOptions  = new()
    {
        {"", ("Do not request any information", null, null)},
        {"Email", ("Request email address only", "Provide your email address", StaticFormEmail )},
        {"Address", ("Request shipping address", "Provide your address", StaticFormAddress)},
    };

    public async Task<SelectList> GetSelect(string storeId ,string selectedFormId)
    {
        var forms = await GetForms(storeId);
        return new SelectList(HardcodedOptions.Select(pair => new SelectListItem(pair.Value.selectText, pair.Key, selectedFormId == pair.Key)).Concat(forms.Select(data => new SelectListItem(data.Name, data.Id, data.Id == selectedFormId))),
            nameof(SelectListItem.Value), nameof(SelectListItem.Text));
    }
    
    
    public async Task<List<FormData>> GetForms(string storeId)
    {
        ArgumentNullException.ThrowIfNull(storeId);
        await using var context = _applicationDbContextFactory.CreateContext();
        return await context.Forms.Where(data => data.StoreId == storeId).ToListAsync();
    }

    public async Task<FormData?> GetForm(string storeId, string id)
    {
        await using var context = _applicationDbContextFactory.CreateContext();
        return await context.Forms.Where(data => data.Id == id && data.StoreId == storeId).FirstOrDefaultAsync();
    }
    public async Task<FormData?> GetForm(string id)
    {
        if (id is null)
        {
            return null;
        }

        if (HardcodedOptions.TryGetValue(id, out var hardcodedForm))
        {
            return new FormData
            {
                Config = hardcodedForm.form.ToString(),
                Id = id,
                Name = hardcodedForm.name,
                Public = false
            };
        }
        await using var context = _applicationDbContextFactory.CreateContext();
        return await context.Forms.Where(data => data.Id == id).FirstOrDefaultAsync();
    }

    public async Task RemoveForm(string id, string storeId)
    {
        await using var context = _applicationDbContextFactory.CreateContext();
        var item = await context.Forms.SingleOrDefaultAsync(data => data.StoreId == storeId && id == data.Id);
        if (item is not null)
            context.Remove(item);
        await context.SaveChangesAsync();
    }

    public async Task AddOrUpdateForm(FormData data)
    {
        await using var context = _applicationDbContextFactory.CreateContext();

        context.Update(data);
        await context.SaveChangesAsync();
    }

    public async Task<bool> Validate(Form form, ModelStateDictionary modelState)
    {
        return _formProviders.Validate(form, modelState);
    }
}
