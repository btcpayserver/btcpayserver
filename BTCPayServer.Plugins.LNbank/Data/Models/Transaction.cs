using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using BTCPayServer.Lightning;

namespace BTCPayServer.Plugins.LNbank.Data.Models
{
    public class Transaction
    {
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public string TransactionId { get; set; }
        public string InvoiceId { get; set; }
        public string WalletId { get; set; }

        [Required]
        public LightMoney Amount { get; set; }
        [DisplayName("Settled amount")]
        public LightMoney AmountSettled { get; set; }
        public string Description { get; set; }
        [DisplayName("Payment Request")]
        [Required]
        public string PaymentRequest { get; set; }
        [DisplayName("Creation date")]
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        [DisplayName("Expiry")]
        public DateTimeOffset ExpiresAt { get; set; }
        [DisplayName("Payment date")]
        public DateTimeOffset? PaidAt { get; set; }

        public Wallet Wallet { get; set; }

        private const string StatusPaid = "paid";
        private const string StatusUnpaid = "unpaid";
        private const string StatusExpired = "expired";

        public string Status
        {
            get
            {
                if (AmountSettled != null)
                {
                    return StatusPaid;
                }

                if (ExpiresAt <= DateTimeOffset.UtcNow)
                {
                    return StatusExpired;
                }

                return StatusUnpaid;
            }
        }

        public LightningInvoiceStatus LightningInvoiceStatus
        {
            get
            {
                switch (Status)
                {
                    case StatusPaid:
                        return LightningInvoiceStatus.Paid;
                    case StatusUnpaid:
                        return LightningInvoiceStatus.Unpaid;
                    case StatusExpired:
                        return LightningInvoiceStatus.Expired;
                    default:
                        throw new NotSupportedException($"'{Status}' cannot be mapped to any LightningInvoiceStatus");
                }
            }
        }

        public bool IsPaid => Status == StatusPaid;
        public bool IsUnpaid => Status != StatusPaid;
        public bool IsExpired => Status == StatusExpired;
        public bool IsOverpaid => Status == StatusPaid && AmountSettled > Amount;
        public bool IsPaidPartially => Status == StatusPaid && AmountSettled < Amount;

        public DateTimeOffset Date => PaidAt ?? CreatedAt;
    }
}
