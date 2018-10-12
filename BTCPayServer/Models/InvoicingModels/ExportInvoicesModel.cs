using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BTCPayServer.Services.Invoices;
using Newtonsoft.Json;

namespace BTCPayServer.Models.InvoicingModels
{
    public class ExportInvoicesModel
    {
        public InvoiceEntity[] Invoices { get; set; }
        public string Format { get; set; }

        public string Process()
        {
            if (String.Equals(Format, "json", StringComparison.OrdinalIgnoreCase))
                return processJson();
            else
                throw new Exception("Export format not supported");
        }

        private string processJson()
        {
            foreach (var i in Invoices)
            {
                // removing error causing complex circular dependencies
                i.Payments?.ForEach(a =>
                {
                    a.Output = null;
                    a.Outpoint = null;
                });
            }

            var serializerSett = new JsonSerializerSettings { ReferenceLoopHandling = ReferenceLoopHandling.Ignore };
            var json = JsonConvert.SerializeObject(new { Invoices }, Formatting.Indented, serializerSett);

            return json;
        }
    }
}
