using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Reflection.Metadata;
using System.Text;
using System.Threading.Tasks;
using BTCPayServer.Migrations;
using Microsoft.EntityFrameworkCore.ChangeTracking.Internal;
using NBitcoin;
using NBitcoin.Altcoins;
using NBitcoin.DataEncoders;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Data
{
    public partial class PaymentData : MigrationInterceptor.IHasMigration
    {
        public bool TryMigrate()
        {
#pragma warning disable CS0618 // Type or member is obsolete
            if (Currency is not null)
                return false;
            if (Blob is not (null or { Length: 0 }))
            {
                Blob2 = MigrationExtensions.Unzip(Blob);
                Blob2 = MigrationExtensions.SanitizeJSON(Blob2);
                Blob = null;
            }
            var blob = JObject.Parse(Blob2);
            if (blob["cryptoPaymentDataType"] is null)
                blob["cryptoPaymentDataType"] = "BTCLike";
            if (blob["cryptoCode"] is null)
                blob["cryptoCode"] = "BTC";

            if (blob["receivedTime"] is null)
                blob.Move(["receivedTimeMs"], ["receivedTime"]);
            else
            {
                // Convert number of seconds to number of milliseconds
                var timeSeconds = (ulong)(long)blob["receivedTime"].Value<long>();
                var date = NBitcoin.Utils.UnixTimeToDateTime(timeSeconds);
                blob["receivedTime"] = DateTimeToMilliUnixTime(date.UtcDateTime);
            }

            var cryptoCode = blob["cryptoCode"].Value<string>();
            MigratedPaymentMethodId = PaymentMethodId;
            PaymentMethodId = cryptoCode + "_" + blob["cryptoPaymentDataType"].Value<string>();
            PaymentMethodId = MigrationExtensions.MigratePaymentMethodId(PaymentMethodId);
            var divisibility = MigrationExtensions.GetDivisibility(PaymentMethodId);
            Currency = blob["cryptoCode"].Value<string>();
            blob.Remove("cryptoCode");
            blob.Remove("cryptoPaymentDataType");

            JObject cryptoData;
            if (blob["cryptoPaymentData"] is null)
            {
                cryptoData = new JObject();
                blob["cryptoPaymentData"] = cryptoData;
                cryptoData["RBF"] = true;
                cryptoData["confirmationCount"] = 0;
            }
            else
            {
                cryptoData = JObject.Parse(blob["cryptoPaymentData"].Value<string>());
                foreach (var prop in cryptoData.Properties().ToList())
                {
                    if (prop.Name is "rbf")
                        cryptoData.RenameProperty("rbf", "RBF");
                    else if (prop.Name is "bolT11")
                        cryptoData.RenameProperty("bolT11", "BOLT11");
                    else
                        cryptoData.RenameProperty(prop.Name, MigrationExtensions.Camel.GetPropertyName(prop.Name, false));
                }
            }
            blob.Remove("cryptoPaymentData");
            cryptoData["outpoint"] = blob["outpoint"];
			if (blob["output"] is not (null or { Type: JTokenType.Null }))
			{
                // Old versions didn't track addresses, so we take it from output.
                // We don't know the network for sure but better having something than nothing in destination.
                // If signet/testnet crash we don't really care anyway.
                // Also, only LTC was supported at this time.
                Network network = (cryptoCode switch { "LTC" => (INetworkSet)Litecoin.Instance, _ => Bitcoin.Instance }).Mainnet;
                var txout = network.Consensus.ConsensusFactory.CreateTxOut();
                txout.ReadWrite(Encoders.Hex.DecodeData(blob["output"].Value<string>()), network);
                cryptoData["value"] = txout.Value.Satoshi;
                blob["destination"] = txout.ScriptPubKey.GetDestinationAddress(network)?.ToString();
            }
            blob.Remove("output");
            blob.Remove("outpoint");
            // Convert from sats to btc
            if (cryptoData["value"] is not (null or { Type: JTokenType.Null }))
            {
                var v = cryptoData["value"].Value<long>();
                Amount = (decimal)v / (decimal)Money.COIN;
                cryptoData.Remove("value");

                blob["paymentMethodFee"] = blob["networkFee"];
                blob.RemoveIfValue<decimal>("paymentMethodFee", 0.0m);
                blob.ConvertNumberToString("paymentMethodFee");
                blob.Remove("networkFee");
                blob.RemoveIfNull("paymentMethodFee");
			}
            // Convert from millisats to btc
            else if (cryptoData["amount"] is not (null or { Type: JTokenType.Null }))
            {
                var v = cryptoData["amount"].Value<long>();
                Amount = (decimal)v / (decimal)Math.Pow(10.0, divisibility);
                cryptoData.Remove("amount");
            }
            if (cryptoData["address"] is not (null or { Type: JTokenType.Null }))
            {
                blob["destination"] = cryptoData["address"];
                cryptoData.Remove("address");
            }
            if (cryptoData["BOLT11"] is not (null or { Type: JTokenType.Null }))
            {
                blob["destination"] = cryptoData["BOLT11"];
                cryptoData.Remove("BOLT11");
            }
            if (cryptoData["outpoint"] is not (null or { Type: JTokenType.Null }))
            {
                // Convert to format txid-n
                cryptoData["outpoint"] = OutPoint.Parse(cryptoData["outpoint"].Value<string>()).ToString();
            }
            if (Accounted is false)
                Status = PaymentStatus.Unaccounted;
            else if (cryptoData["confirmationCount"] is { Type: JTokenType.Integer })
            {
                var confirmationCount = cryptoData["confirmationCount"].Value<int>();
                // Technically, we should use the invoice's speed policy, however it's not on our
                // scope and is good enough for majority of cases.
                Status = confirmationCount > 0 ? PaymentStatus.Settled : PaymentStatus.Processing;
                if (cryptoData["LockTime"] is { Type: JTokenType.Integer })
                {
                    var lockTime = cryptoData["LockTime"].Value<int>();
                    if (confirmationCount < lockTime)
                        Status = PaymentStatus.Processing;
                }
            }
            else
            {
                Status = PaymentStatus.Settled;
            }
			Created = MilliUnixTimeToDateTime(blob["receivedTime"].Value<long>());
            cryptoData.RemoveIfValue<bool>("rbf", false);
            cryptoData.Remove("legacy");
            cryptoData.Remove("networkFee");
            cryptoData.Remove("paymentType");
            cryptoData.RemoveIfNull("outpoint");
            cryptoData.RemoveIfValue<bool>("RBF", false);

            blob.Remove("receivedTime");
            blob.Remove("accounted");
            blob.Remove("networkFee");
            blob["details"] = cryptoData;
            blob["divisibility"] = divisibility;
            blob["version"] = 2;
			Blob2 = blob.ToString(Formatting.None);
            Accounted = null;
#pragma warning restore CS0618 // Type or member is obsolete
            return true;
        }
        [NotMapped]
        public bool Migrated { get; set; }
        [NotMapped]
        [EditorBrowsable(EditorBrowsableState.Never)]
        public string MigratedPaymentMethodId { get; set; }

        static readonly DateTimeOffset unixRef = new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public static long DateTimeToMilliUnixTime(in DateTime time)
        {
            var date = ((DateTimeOffset)time).ToUniversalTime();
            long v = (long)(date - unixRef).TotalMilliseconds;
            if (v < 0)
                throw new FormatException("Invalid datetime (less than 1/1/1970)");
            return v;
        }
		public static DateTimeOffset MilliUnixTimeToDateTime(long value)
		{
			var v = value;
			if (v < 0)
				throw new FormatException("Invalid datetime (less than 1/1/1970)");
			return unixRef + TimeSpan.FromMilliseconds(v);
		}
	}
}
