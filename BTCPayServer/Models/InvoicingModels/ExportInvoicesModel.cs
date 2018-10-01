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
        public InvoiceEntity[] List { get; set; }
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
            var sb = new StringBuilder();

            sb.AppendLine("{ \"Invoices\" : [");

            var serializerSett = new JsonSerializerSettings { ReferenceLoopHandling = ReferenceLoopHandling.Ignore };

            var index = 0;
            foreach (var i in List)
            {
                if (index++ > 0)
                    sb.Append(",");
                
                // removing error causing complex circular dependencies
                i.Payments?.ForEach(a =>
                {
                    a.Output = null;
                    a.Outpoint = null;
                });
                //

                var json = JsonConvert.SerializeObject(i, Formatting.Indented, serializerSett);
                sb.AppendLine(json);
            }
            sb.AppendLine("] }");

            return sb.ToString();
        }
    }
}
