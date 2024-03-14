using System.Collections.Generic;
using BTCPayServer.Services.Reporting;

namespace BTCPayServer.Services
{
    public class ReportService
    {
        public ReportService(IEnumerable<ReportProvider> reportProviders)
        {
            foreach (var r in reportProviders)
            {
                ReportProviders.TryAdd(r.Name, r);
            }
        }

        public Dictionary<string, ReportProvider> ReportProviders { get; } = new();
    }
}
