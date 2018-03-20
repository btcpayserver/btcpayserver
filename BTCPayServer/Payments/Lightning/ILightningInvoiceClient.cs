using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Payments.Lightning.Charge;

namespace BTCPayServer.Payments.Lightning
{
    public class LightningInvoice
    {
        public string Id { get; set; }
        public string Status { get; set; }
        public string BOLT11 { get; set; }
        public DateTimeOffset? PaidAt
        {
            get; set;
        }
        public LightMoney Amount { get; set; }
    }

    public class LightningNodeInformation
    {
        public string Address { get; internal set; }
        public int P2PPort { get; internal set; }
        public int BlockHeight { get; set; }
    }
    public interface ILightningInvoiceClient
    {
        Task<LightningInvoice> GetInvoice(string invoiceId, CancellationToken cancellation = default(CancellationToken));
        Task<LightningInvoice> CreateInvoice(LightMoney amount, TimeSpan expiry, CancellationToken cancellation = default(CancellationToken));
        Task<ILightningListenInvoiceSession> Listen(CancellationToken cancellation = default(CancellationToken));
        Task<LightningNodeInformation> GetInfo(CancellationToken cancellation = default(CancellationToken));
    }

    public interface ILightningListenInvoiceSession : IDisposable
    {
        Task<LightningInvoice> WaitInvoice(CancellationToken cancellation);
    }
}
