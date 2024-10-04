using System.Collections.Generic;
using System.Linq;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Client.Models;
using BTCPayServer.Payments;

namespace BTCPayServer.Services.Altcoins.Monero.Services
{
    public class MoneroSyncSummaryProvider : ISyncSummaryProvider
    {
        private readonly MoneroRPCProvider _moneroRpcProvider;

        public MoneroSyncSummaryProvider(MoneroRPCProvider moneroRpcProvider)
        {
            _moneroRpcProvider = moneroRpcProvider;
        }

        public bool AllAvailable()
        {
            return _moneroRpcProvider.Summaries.All(pair => pair.Value.WalletAvailable);
        }

        public string Partial { get; } = "Monero/MoneroSyncSummary";
        public IEnumerable<ISyncStatus> GetStatuses()
        {
            return _moneroRpcProvider.Summaries.Select(pair => new MoneroSyncStatus()
            {
                Summary = pair.Value, PaymentMethodId = PaymentMethodId.Parse(pair.Key)
            });
        }
    }

    public class MoneroSyncStatus: SyncStatus, ISyncStatus
    {
        public new PaymentMethodId PaymentMethodId
        {
            get => PaymentMethodId.Parse(base.PaymentMethodId);
            set => base.PaymentMethodId = value.ToString();
        }
        public override bool Available
        {
            get
            {
                return Summary?.WalletAvailable ?? false;
            }
        }

        public MoneroRPCProvider.MoneroLikeSummary Summary { get; set; }
    }
}
