using System;
using System.Collections.Generic;
using BTCPayServer.Data;
using BTCPayServer.Services.Rates;
using BTCPayServer.Views;

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
            var blob = data.GetBlob();
            Archived = data.Archived;
            Title = blob.View.Title;
            Amount = blob.Limit;
            Currency = blob.Currency;
            Description = blob.View.Description;
            ExpiryDate = data.EndDate is DateTimeOffset dt ? (DateTime?)dt.UtcDateTime : null;
            Email = blob.View.Email;
            MinimumClaim = blob.MinimumClaim;
            EmbeddedCSS = blob.View.EmbeddedCSS;
            CustomCSSLink = blob.View.CustomCSSLink;
            if (!string.IsNullOrEmpty(EmbeddedCSS))
                EmbeddedCSS = $"<style>{EmbeddedCSS}</style>";
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
        public string AmountDueFormatted { get; set; }
        public decimal Amount { get; set; }
        public string Id { get; set; }
        public string Currency { get; set; }
        public DateTime? ExpiryDate { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string EmbeddedCSS { get; set; }
        public string CustomCSSLink { get; set; }
        public List<PayoutLine> Payouts { get; set; } = new List<PayoutLine>();
        public DateTimeOffset StartDate { get; set; }
        public DateTime LastRefreshed { get; set; }
        public CurrencyData CurrencyData { get; set; }
        public string AmountCollectedFormatted { get; set; }
        public string AmountFormatted { get; set; }
        public bool Archived { get; set; }

        public class PayoutLine
        {
            public string Id { get; set; }
            public decimal Amount { get; set; }
            public string AmountFormatted { get; set; }
            public string Status { get; set; }
            public string Destination { get; set; }
            public string Currency { get; set; }
            public string Link { get; set; }
            public string TransactionId { get; set; }
        }
    }
}
