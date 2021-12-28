using System;
using System.ComponentModel.DataAnnotations;
using BTCPayServer.Client.JsonConverters;
using BTCPayServer.Lightning;
using NBitcoin;
using NBitcoin.JsonConverters;
using Newtonsoft.Json;

namespace BTCPayServer.Plugins.LNbank.Services
{
    public class LightningInvoiceCreateRequest
    {
        public static readonly TimeSpan ExpiryDefault = TimeSpan.FromDays(1);
        public string Description { get; set; }
        [JsonConverter(typeof(UInt256JsonConverter))]
        public uint256 DescriptionHash { get; set; }
        [Required]
        [JsonConverter(typeof(LightMoneyJsonConverter))]
        public LightMoney Amount { get; set; }
        public TimeSpan Expiry { get; set; } = ExpiryDefault;
    }
}
