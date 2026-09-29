using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
                ReportProviders.TryAdd(r.Name, r);
            }
        }

        public Dictionary<string, ReportProvider> ReportProviders { get; } = new(StringComparer.OrdinalIgnoreCase);

        public async Task<StoreReportResponse> Query(string storeId, ReportProvider report, DateTimeOffset from,
            DateTimeOffset to, CancellationToken cancellationToken = default)
        {
            var ctx = new QueryContext(storeId, from, to);
            await report.Query(ctx, cancellationToken);
            ResizeRows(ctx.ViewDefinition?.Fields.Count ?? 0, ctx.Data);
            return new StoreReportResponse
            {
                ReportName = report.Name,
                Fields = ctx.ViewDefinition?.Fields ?? [],
                Charts = ctx.ViewDefinition?.Charts ?? [],
                Data = ctx.Data.Select(d => new Newtonsoft.Json.Linq.JArray(d)).ToList(),
                From = from,
                To = to
            };
        }

        public static (DateTimeOffset From, DateTimeOffset To) ResolveRange(SearchString search,
            TimeZoneInfo defaultTimeZone, DateTimeOffset now)
        {
            var range = search.GetDateRange(defaultTimeZone);
            var to = range.EndDate ?? now;
            var from = search.GetFilterString("daterange") == "alltime"
                ? DateTimeOffset.UnixEpoch
                : range.StartDate ?? to.AddMonths(-1);
            return (from, to);
        }

        private static void ResizeRows(int fieldsCount, IList<IList<object>> data)
        {
            foreach (var row in data)
            {
                while (row.Count < fieldsCount)
                    row.Add(null);
                while (row.Count > fieldsCount)
                    row.RemoveAt(row.Count - 1);
            }
        }
    }
}
