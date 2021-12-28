namespace BTCPayServer.Plugins.LNbank.Services.Wallets
{
    public class TransactionQuery
    {
        public string UserId { get; set; }
        public string WalletId { get; set; }
        public string InvoiceId { get; set; }
        public string TransactionId { get; set; }
        public string PaymentRequest { get; set; }
        public bool HasInvoiceId { get; set; }
    }
}
