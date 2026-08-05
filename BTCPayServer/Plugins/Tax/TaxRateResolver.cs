using System.Linq;
using BTCPayServer.Data;

namespace BTCPayServer.Plugins.Tax;

public class TaxRateResolver
{
    public StoreTaxRate? Resolve(StoreBlob storeBlob, string? taxRateId)
    {
        if (string.IsNullOrEmpty(taxRateId))
            return null;

        return storeBlob.TaxRates?.FirstOrDefault(r => r.Id == taxRateId);
    }

    public StoreTaxRate? GetSuggestedDefault(StoreBlob storeBlob) => storeBlob.TaxRates?.FirstOrDefault(r => r.IsDefault) ?? storeBlob.TaxRates?.FirstOrDefault();
}
