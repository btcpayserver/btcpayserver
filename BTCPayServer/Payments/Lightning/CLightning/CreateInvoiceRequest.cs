using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Payments.Lightning.CLightning
{
    public class CreateInvoiceRequest
    {
        public LightMoney Amont { get; set; }
        public TimeSpan Expiry { get; set; }
    }
}
