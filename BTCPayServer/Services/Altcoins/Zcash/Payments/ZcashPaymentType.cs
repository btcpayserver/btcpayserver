#if ALTCOINS
using System.Globalization;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.Altcoins;
using BTCPayServer.Services.Invoices;
using NBitcoin;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Services.Altcoins.Zcash.Payments
{
    internal class ZcashPaymentType
    {
        internal static readonly PaymentType Instance = new("ZcashLike");
    }
}
#endif
