using System;
using BTCPayServer.Client.JsonConverters;
using BTCPayServer.JsonConverters;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Client.Models
{
    public class CreateInvoiceRequest : InvoiceDataBase
    {
        public string[] AdditionalSearchTerms { get; set; }
    }
}
