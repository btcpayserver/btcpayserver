#nullable enable
using System.Collections.Generic;
using System.Linq;
using BTCPayServer.Abstractions.Custodians;

namespace BTCPayServer.Abstractions.Extensions;

public static class CustodianExtensions
{
    public static ICustodian? GetCustodianByCode(this IEnumerable<ICustodian> custodians, string code)
    {
        return custodians.FirstOrDefault(custodian => custodian.Code == code);
    }
}
