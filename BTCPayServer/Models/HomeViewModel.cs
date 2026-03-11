using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.Models;

public class HomeViewModel
{
    [Display(Name = "Has Store")]
    public bool HasStore { get; set; }
}
