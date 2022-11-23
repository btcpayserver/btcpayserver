using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Form;
using BTCPayServer.Data;
using BTCPayServer.Data.Data;
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

    public class FormQuery
    {
        public string[] Stores { get; set; }
        public string[] Ids { get; set; }
    }

    public async Task<List<FormData>> GetForms(FormQuery query)
    {
        await using var context = _applicationDbContextFactory.CreateContext();
        return await GetForms(query, context);
    }

    private async Task<List<FormData>> GetForms(FormQuery query, ApplicationDbContext context)
    {
        var queryable = context.Forms.AsQueryable();

        if (query.Stores is not null)
        {
            queryable = queryable.Where(data => query.Stores.Contains(data.StoreId));
        }
        if (query.Ids is not null)
        {
            queryable = queryable.Where(data => query.Ids.Contains(data.Id));
        }

        return await queryable.ToListAsync();
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
        Fields = new List<Field>() {new HtmlInputField("Enter your email", "buyerEmail", null, true, null)}
    };

    public static readonly Form StaticFormAddress = new()
    {
        Fields = new List<Field>()
        {
            new HtmlInputField("Enter your email", "buyerEmail", null, true, null, "email"),
            new HtmlInputField("Name", "buyerName", null, true, null),
            new HtmlInputField("Address Line 1", "buyerAddress1", null, true, null),
            new HtmlInputField("Address Line 2", "buyerAddress2", null, false, null),
            new HtmlInputField("City", "buyerCity", null, true, null),
            new HtmlInputField("Postcode", "buyerZip", null, false, null),
            new HtmlInputField("State", "buyerState", null, false, null),
            new HtmlInputField("Country", "buyerCountry", null, true, null)
        }
    };
}
