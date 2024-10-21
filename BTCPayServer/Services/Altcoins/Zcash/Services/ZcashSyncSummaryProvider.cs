using System.Collections.Generic;
using System.Linq;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Client.Models;
using BTCPayServer.Payments;

namespace BTCPayServer.Services.Altcoins.Zcash.Services
{
    public class ZcashSyncSummaryProvider : ISyncSummaryProvider
    {
        private readonly ZcashRPCProvider _ZcashRpcProvider;

        public ZcashSyncSummaryProvider(ZcashRPCProvider ZcashRpcProvider)
        {
            _ZcashRpcProvider = ZcashRpcProvider;
        }

        public bool AllAvailable()
        {
            return _ZcashRpcProvider.Summaries.All(pair => pair.Value.WalletAvailable);
        }

        public string Partial { get; } = "Zcash/ZcashSyncSummary";
        public IEnumerable<ISyncStatus> GetStatuses()
        {
            return _ZcashRpcProvider.Summaries.Select(pair => new ZcashSyncStatus()
            {
                Summary = pair.Value, PaymentMethodId = PaymentMethodId.Parse(pair.Key)
            });
        }
    }

    public class ZcashSyncStatus: SyncStatus, ISyncStatus
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

        public ZcashRPCProvider.ZcashLikeSummary Summary { get; set; }
    }
}
