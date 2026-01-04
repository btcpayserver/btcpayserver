#nullable enable
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Services.Invoices;
using NBitcoin;

namespace BTCPayServer.Payments
{
    public interface ICheckoutCheatModeExtension
    {
        public class PayInvoiceResult
        {
            public PayInvoiceResult(string transactionId)
            {
                TransactionId = transactionId;
            }
            public string TransactionId { get; set; }
            public decimal? AmountRemaining { get; set; }
            public string? SuccessMessage { get; set; }
        }
        public class MineBlockResult
        {
            public MineBlockResult()
            {
            }
            public MineBlockResult(string? successMessage)
            {
                SuccessMessage = successMessage;
            }
            public string? SuccessMessage { get; set; }
        }
        public class MineBlockContext
        {
            public int BlockCount { get; set; }
        }
        public class PayInvoiceContext
        {
            public PayInvoiceContext(InvoiceEntity invoice, decimal amount, StoreData store, PaymentPrompt paymentMethod, object details)
            {
                this.Invoice = invoice;
                this.Amount = amount;
                this.Store = store;
                PaymentPrompt = paymentMethod;
                PaymentPromptDetails = details;
            }

            public InvoiceEntity Invoice { get; }
            public decimal Amount { get; }
            public StoreData Store { get; }
            public PaymentPrompt PaymentPrompt { get; }
            public object? PaymentPromptDetails { get; }
        }
        public bool Handle(PaymentMethodId paymentMethodId);
        Task<PayInvoiceResult> PayInvoice(PayInvoiceContext payInvoiceContext);
        Task<MineBlockResult> MineBlock(MineBlockContext mineBlockContext);
    }
}
