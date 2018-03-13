using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Services.Invoices;
using NBitcoin;
using Newtonsoft.Json;

namespace BTCPayServer.Payments.Bitcoin
{
    public class BitcoinLikeOnChainPaymentMethod : IPaymentMethodDetails
    {
        public PaymentTypes GetPaymentType()
        {
            return PaymentTypes.BTCLike;
        }

        public string GetPaymentDestination()
        {
            return DepositAddress;
        }

        public decimal GetTxFee()
        {
            return TxFee.ToDecimal(MoneyUnit.BTC);
        }

        public void SetNoTxFee()
        {
            TxFee = Money.Zero;
        }


        public void SetPaymentDestination(string newPaymentDestination)
        {
            DepositAddress = newPaymentDestination;
        }

        // Those properties are JsonIgnore because their data is inside CryptoData class for legacy reason
        [JsonIgnore]
        public FeeRate FeeRate { get; set; }
        [JsonIgnore]
        public Money TxFee { get; set; }
        [JsonIgnore]
        public String DepositAddress { get; set; }
        public BitcoinAddress GetDepositAddress(Network network)
        {
            return string.IsNullOrEmpty(DepositAddress) ? null : BitcoinAddress.Create(DepositAddress, network);
        }
        ///////////////////////////////////////////////////////////////////////////////////////
    }
}
