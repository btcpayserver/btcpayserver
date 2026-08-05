using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.Plugins.Tax;

public class StoreTaxRatesViewModel
{
    public List<StoreTaxRate> TaxRates { get; set; } = new();
    public StoreTaxRateEditViewModel NewRate { get; set; } = new();
}

public class StoreTaxRateEditViewModel
{
    public string? Id { get; set; }

    [Required]
    [MaxLength(50)]
    public string? Name { get; set; }

    [Range(0, 100)]
    public decimal Rate { get; set; }
}
