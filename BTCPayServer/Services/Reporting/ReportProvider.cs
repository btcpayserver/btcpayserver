#nullable enable
using System.Threading.Tasks;
using System.Threading;

namespace BTCPayServer.Services.Reporting
{
    public abstract class ReportProvider
    {
        public virtual bool IsAvailable()
        {
            return true;
        }
        public abstract string Name { get; }
        public abstract Task Query(QueryContext queryContext, CancellationToken cancellation);
    }
}
