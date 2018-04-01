using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Payments.Lightning.Charge
{
    public class CreateInvoiceRequest
    {
        public LightMoney Amont { get; set; }
        public TimeSpan Expiry { get; set; }
    }
}
