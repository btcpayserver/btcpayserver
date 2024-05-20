using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BTCPayServer.Data
{
    public partial class PayoutData
    {
        public void Migrate()
        {
            PaymentMethodId = MigrationExtensions.MigratePaymentMethodId(PaymentMethodId);
            // Could only be BTC-LN or BTC-CHAIN, so we extract the crypto currency
            Currency = PaymentMethodId.Split('-')[0];
        }
    }
}
