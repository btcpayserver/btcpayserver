#nullable enable
using System.Threading.Tasks;
using System.Threading;
using BTCPayServer.Data;
using System;
using System.Linq;

namespace BTCPayServer.Services.Reporting
{
    public abstract class ReportProvider
    {
        public abstract ViewDefinition[] CreateViewDefinitions();
        public virtual bool IsAvailable()
        {
            return true;
        }
        public abstract Task Query(QueryContext queryContext, CancellationToken cancellation);

        public ViewDefinition CreateViewDefinition(string viewName)
        {
            return CreateViewDefinitions().First(v => v.Name == viewName);
        }
    }
}
