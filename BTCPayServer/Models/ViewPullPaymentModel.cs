using System;
using System.Collections.Generic;
using System.Linq;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Payments;
using BTCPayServer.Payouts;
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
            PayoutMethodIds = blob.SupportedPayoutMethods;
            BitcoinOnly = blob.SupportedPayoutMethods.All(p => p == PayoutTypes.CHAIN.GetPayoutMethodId("BTC") || p == PayoutTypes.LN.GetPayoutMethodId("BTC"));
            SelectedPayoutMethod = PayoutMethodIds.First().ToString();
            Archived = data.Archived;
            AutoApprove = blob.AutoApproveClaims;
            Title = blob.View.Title;
            Description = blob.View.Description;
            Amount = data.Limit;
            Currency = data.Currency;
            Description = blob.View.Description;
            ExpiryDate = data.EndDate is DateTimeOffset dt ? (DateTime?)dt.UtcDateTime : null;
            Email = blob.View.Email;
            MinimumClaim = blob.MinimumClaim;
            IsPending = !data.IsExpired(now);
            if (data.Archived)
            {
                Status = "Archived";
            }
            else if (data.IsExpired(now))
            {
                Status = "Expired";
            }
            else if (!data.HasStarted(now))
            {
                Status = "Not yet started";
            }
            else
            {
                Status = string.Empty;
            }

            EndsIn = string.Empty;
            if (data.EndsIn(now) is TimeSpan e)
            {
                EndsIn = e.TimeString();
            }
        }

        public bool BitcoinOnly { get; set; }

        public string StoreId { get; set; }

        public string SelectedPayoutMethod { get; set; }

        public PayoutMethodId[] PayoutMethodIds { get; set; }

        public string SetupDeepLink { get; set; }
        public string ResetDeepLink { get; set; }

        public string HubPath { get; set; }
        public string EndsIn { get; set; }
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
