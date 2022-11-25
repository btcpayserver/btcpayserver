#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Form;
using BTCPayServer.Data;
using BTCPayServer.Data.Data;
using BTCPayServer.Services.Stores;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Forms;

public class FormDataService
{
    private readonly ApplicationDbContextFactory _applicationDbContextFactory;

    public FormDataService(
        ApplicationDbContextFactory applicationDbContextFactory)
    {
        _applicationDbContextFactory = applicationDbContextFactory;
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
        if (id == GenericFormOption.Address.ToString())
            return new FormData
            {
                Config = FormDataService.StaticFormAddress.ToString(),
                Id = GenericFormOption.Address.ToString(),
                Name = "Provide your address",
            };
        if (id == GenericFormOption.Email.ToString())
            return new FormData
            {
                Config = FormDataService.StaticFormEmail.ToString(),
                Id = GenericFormOption.Email.ToString(),
                Name = "Provide your email address",
            };
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

    public static readonly Form StaticFormEmail = new()
    {
        Fields = new List<Field>() {Field.Create("Enter your email", "buyerEmail", null, true, null, "email")}
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
}
