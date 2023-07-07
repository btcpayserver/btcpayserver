using System.Collections.Generic;
using System.Linq;
using BTCPayServer.Client.Models;
using BTCPayServer.Services.Reporting;

namespace BTCPayServer.Services
{
    public class ReportService
    {
        public ReportService(IEnumerable<ReportProvider> reportProviders)
        {
            foreach (var r in reportProviders)
            {
                foreach (var definition in r.CreateViewDefinitions())
                {
                    ReportProviders.Add(definition.Name, r);
                }
            }
        }

        public Dictionary<string, ReportProvider> ReportProviders { get; } = new Dictionary<string, ReportProvider>();
    }
}
