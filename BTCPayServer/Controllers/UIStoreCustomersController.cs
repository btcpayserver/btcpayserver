#nullable enable
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Client;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Data;
using BTCPayServer.Views.UIStoreCustomers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Npgsql;

namespace BTCPayServer.Controllers;

[Authorize(Policy = Policies.CanViewCustomers, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
[AutoValidateAntiforgeryToken]
public class UIStoreCustomersController(
    ApplicationDbContextFactory dbContextFactory,
    IStringLocalizer stringLocalizer) : Controller
{
    [HttpGet("stores/{storeId}/customers")]
    public async Task<IActionResult> ListCustomers(string storeId, string? searchTerm)
    {
        var vm = new ListCustomersViewModel();
        await using var ctx = dbContextFactory.CreateContext();

        var users = ctx.Customers.Include(x => x.CustomerIdentities).Where(c => c.StoreId == storeId);
        if (!string.IsNullOrEmpty(searchTerm))
            users = users.Where(u => (u.CustomerIdentities.Any(c => c.Value == searchTerm)) ||
                                      u.Name.Contains(searchTerm) ||
                                      (u.ExternalRef != null && u.ExternalRef.Contains(searchTerm)));
        vm.Data = await users.ToListAsync();
        return View(vm);
    }

    [HttpGet("stores/{storeId}/customers/add")]
    public IActionResult AddCustomer(string storeId)
    {
        return View("AddEditCustomer", new AddEditCustomerViewModel(new()));
    }

    IActionResult AddEditCustomerView() => View("AddEditCustomer");
    [HttpPost("stores/{storeId}/customers/add")]
    public async Task<IActionResult> AddCustomer(string storeId, AddEditCustomerViewModel vm)
    {
        vm.Data.StoreId = storeId;
        if (!ModelState.IsValid)
            return AddEditCustomerView();
        vm.FillData();
        await using var ctx = dbContextFactory.CreateContext();
        ctx.Customers.Add(vm.Data);
        try
        {
            await ctx.SaveChangesAsync();
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException
                                          {
                                              SqlState: PostgresErrorCodes.UniqueViolation
                                          } pg)
        {
            var message = pg.ConstraintName switch {
                "IX_customers_store_id_email" => ("Email", stringLocalizer["This email is already used by another customer"]),
                "IX_customers_store_id_external_ref" => ("Data.ExternalRef", stringLocalizer["This external reference is already used by another customer"]),
                _ => throw e
            };
            ModelState.AddModelError(message.Item1, message.Item2);
            return AddEditCustomerView();
        }
        TempData.SetStatusSuccess(stringLocalizer["Customer added successfully"]);

        return RedirectToAction(nameof(ListCustomers), new { storeId });
    }
}
