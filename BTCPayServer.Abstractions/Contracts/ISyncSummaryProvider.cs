using System.Collections.Generic;

namespace BTCPayServer.Abstractions.Contracts
{
    public interface ISyncSummaryProvider
    {
        bool AllAvailable();

        string Partial { get; }
        IEnumerable<ISyncStatus> GetStatuses();
    }

    public interface ISyncStatus
    {
        public string PaymentMethodId { get; set; }
        public bool Available { get; }
    }
}
