using System;
using System.Collections.Generic;
using System.Linq;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Payments;
using BTCPayServer.Services.Rates;
using PullPaymentData = BTCPayServer.Data.PullPaymentData;

namespace BTCPayServer.Models
{
    public class ViewPullPaymentModel
    {
        public ViewPullPaymentModel()
        {

        }
        public ViewPullPaymentModel(PullPaymentData data, DateTimeOffset now)
        {
            Id = data.Id;
            StoreId = data.StoreId;
            var blob = data.GetBlob();
            PaymentMethods = blob.SupportedPaymentMethods;
            BitcoinOnly = blob.SupportedPaymentMethods.All(p => p.CryptoCode == "BTC");
            SelectedPaymentMethod = PaymentMethods.First().ToString();
            Archived = data.Archived;
            AutoApprove = blob.AutoApproveClaims;
            Title = blob.View.Title;
            Description = blob.View.Description;
            Amount = blob.Limit;
            Currency = blob.Currency;
            Description = blob.View.Description;
            ExpiryDate = data.EndDate is DateTimeOffset dt ? (DateTime?)dt.UtcDateTime : null;
            Email = blob.View.Email;
            MinimumClaim = blob.MinimumClaim;
            IsPending = !data.IsExpired();
            var period = data.GetPeriod(now);
            if (data.Archived)
            {
                Status = "Archived";
            }
            else if (data.IsExpired())
            {
                Status = "Expired";
            }
            else if (period is null)
            {
                Status = "Not yet started";
            }
            else
            {
                Status = string.Empty;
            }

            ResetIn = string.Empty;
            if (period?.End is DateTimeOffset pe)
            {
                var resetIn = (pe - DateTimeOffset.UtcNow);
                if (resetIn < TimeSpan.Zero)
                    resetIn = TimeSpan.Zero;
                ResetIn = resetIn.TimeString();
            }
        }

        public bool BitcoinOnly { get; set; }

        public string StoreId { get; set; }

        public string SelectedPaymentMethod { get; set; }

        public PaymentMethodId[] PaymentMethods { get; set; }

        public string HubPath { get; set; }
        public string ResetIn { get; set; }
        public string Email { get; set; }
        public string Status { get; set; }
        public bool IsPending { get; set; }
        public decimal AmountCollected { get; set; }
        public decimal AmountDue { get; set; }
        public decimal ClaimedAmount { get; set; }
        public decimal MinimumClaim { get; set; }
        public string Destination { get; set; }
        public decimal Amount { get; set; }
        public string Id { get; set; }
        public string Currency { get; set; }
        public DateTime? ExpiryDate { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public List<PayoutLine> Payouts { get; set; } = new();
        public DateTimeOffset StartDate { get; set; }
        public DateTime LastRefreshed { get; set; }
        public CurrencyData CurrencyData { get; set; }
        public Uri LnurlEndpoint { get; set; }
        public StoreBrandingViewModel StoreBranding { get; set; }
        public bool Archived { get; set; }
        public bool AutoApprove { get; set; }

        public class PayoutLine
        {
            public string Id { get; set; }
            public decimal Amount { get; set; }
            public string AmountFormatted { get; set; }
            public PayoutState Status { get; set; }
            public string Destination { get; set; }
            public string Currency { get; set; }
            public string Link { get; set; }
            public string TransactionId { get; set; }
            public PaymentMethodId PaymentMethod { get; set; }
        }
    }
}
