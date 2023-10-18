using System.Collections.Generic;
using BTCPayServer.Client.Models;

namespace BTCPayServer.Services.Reporting
{
    public class ViewDefinition
    {
        public IList<StoreReportResponse.Field> Fields
        {
            get;
            set;
        } = new List<StoreReportResponse.Field>();

        public List<ChartDefinition> Charts { get; set; } = new ();
    }
}
