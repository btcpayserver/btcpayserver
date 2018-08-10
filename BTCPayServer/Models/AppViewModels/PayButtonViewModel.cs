using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Models.AppViewModels
{
    public class PayButtonViewModel
    {
        public decimal Price { get; set; }
        public string Currency { get; set; }
        public string CheckoutDesc { get; set; }
        public int ButtonSize { get; set; }
        public string ServerIpn { get; set; }
        public string BrowserRedirect { get; set; }
        public string EmailToNotify { get; set; }
    }
}
