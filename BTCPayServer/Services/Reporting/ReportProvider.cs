#nullable enable
using System.Threading.Tasks;
using System.Threading;
using BTCPayServer.Data;

namespace BTCPayServer.Services.Reporting
{
    public abstract class ReportProvider
    {
        public abstract ViewDefinition CreateViewDefinition();
        public virtual bool IsAvailable()
        {
            return true;
        }
        public abstract Task Query(QueryContext queryContext, CancellationToken cancellation);
    }
}
