using System.ComponentModel.DataAnnotations;
using BTCPayServer.Data;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace BTCPayServer.Views.UIStoreCustomers;

public class AddEditCustomerViewModel
{
    public AddEditCustomerViewModel()
    {

    }

    public AddEditCustomerViewModel(CustomerData data)
    {
        Email = data.Email;
        Data = data;
    }
    public void FillData()
    {
        Data.Email = Email;
    }
    [Required]
    [EmailAddress]
    public string Email { get; set; }
    [ValidateNever]
    public CustomerData Data { get; set; }
}
