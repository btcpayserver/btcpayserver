using NBitcoin;
using System.Linq;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;
using BTCPayServer.Models;
using NBitpayClient;
using Newtonsoft.Json.Linq;
using NBitcoin.DataEncoders;

namespace BTCPayServer.Services.Invoices
{
	public class BuyerInformation
	{
		[JsonProperty(PropertyName = "buyerName")]
		public string BuyerName
		{
			get; set;
		}
		[JsonProperty(PropertyName = "buyerEmail")]
		public string BuyerEmail
		{
			get; set;
		}
		[JsonProperty(PropertyName = "buyerCountry")]
		public string BuyerCountry
		{
			get; set;
		}
		[JsonProperty(PropertyName = "buyerZip")]
		public string BuyerZip
		{
			get; set;
		}
		[JsonProperty(PropertyName = "buyerState")]
		public string BuyerState
		{
			get; set;
		}
		[JsonProperty(PropertyName = "buyerCity")]
		public string BuyerCity
		{
			get; set;
		}
		[JsonProperty(PropertyName = "buyerAddress2")]
		public string BuyerAddress2
		{
			get; set;
		}
		[JsonProperty(PropertyName = "buyerAddress1")]
		public string BuyerAddress1
		{
			get; set;
		}

		[JsonProperty(PropertyName = "buyerPhone")]
		public string BuyerPhone
		{
			get; set;
		}
	}

	public class ProductInformation
	{
		[JsonProperty(PropertyName = "itemDesc")]
		public string ItemDesc
		{
			get; set;
		}
		[JsonProperty(PropertyName = "itemCode")]
		public string ItemCode
		{
			get; set;
		}
		[JsonProperty(PropertyName = "physical")]
		public bool Physical
		{
			get; set;
		}

		[JsonProperty(PropertyName = "price")]
		public double Price
		{
			get; set;
		}

		[JsonProperty(PropertyName = "currency")]
		public string Currency
		{
			get; set;
		}
	}

	public enum SpeedPolicy
	{
		HighSpeed = 0,
		MediumSpeed = 1,
		LowSpeed = 2
	}
	public class InvoiceEntity
	{
		public string Id
		{
			get; set;
		}
		public string StoreId
		{
			get; set;
		}

		public int GetTxCount()
		{
			return Calculate().TxCount;
		}

		public string OrderId
		{
			get; set;
		}

		public Money GetTotalCryptoDue()
		{
			return Calculate().TotalDue;
		}

		private (Money TotalDue, Money Paid, int TxCount) Calculate()
		{
			var totalDue = Money.Coins((decimal)(ProductInformation.Price / Rate)) + TxFee;
			var paid = Money.Zero;
			int txCount = 1;
			var payments =
				Payments
				.OrderByDescending(p => p.ReceivedTime)
				.Select(_ =>
				{
					paid += _.Output.Value;
					return _;
				})
				.TakeWhile(_ =>
				{
					var paidEnough = totalDue <= paid;
					if(!paidEnough)
					{
						txCount++;
						totalDue += TxFee;
					}
					return !paidEnough;
				})
				.ToArray();
			return (totalDue, paid, txCount);
		}

		public Money GetTotalPaid()
		{
			return Calculate().Paid;
		}
		public Money GetCryptoDue()
		{
			var o = Calculate();
			var v = o.TotalDue - o.Paid;
			return v < Money.Zero ? Money.Zero : v;
		}

		public SpeedPolicy SpeedPolicy
		{
			get; set;
		}
		public double Rate
		{
			get; set;
		}
		public DateTimeOffset InvoiceTime
		{
			get; set;
		}
		public DateTimeOffset ExpirationTime
		{
			get; set;
		}
		public BitcoinAddress DepositAddress
		{
			get; set;
		}
		public ProductInformation ProductInformation
		{
			get; set;
		}
		public BuyerInformation BuyerInformation
		{
			get; set;
		}
		public string PosData
		{
			get;
			set;
		}
		public string DerivationStrategy
		{
			get;
			set;
		}
		public string Status
		{
			get;
			set;
		}
		public string ExceptionStatus
		{
			get; set;
		}
		public List<PaymentEntity> Payments
		{
			get; set;
		}
		public bool Refundable
		{
			get;
			set;
		}
		public string RefundMail
		{
			get;
			set;
		}
		public string RedirectURL
		{
			get;
			set;
		}
		public Money TxFee
		{
			get;
			set;
		}
		public bool FullNotifications
		{
			get;
			set;
		}
		public string NotificationURL
		{
			get;
			set;
		}
		public string ServerUrl
		{
			get;
			set;
		}

		public bool IsExpired()
		{
			return DateTimeOffset.UtcNow > ExpirationTime;
		}


		public InvoiceResponse EntityToDTO()
		{
			ServerUrl = ServerUrl ?? "";
			InvoiceResponse dto = new InvoiceResponse
			{
				Id = Id,
				OrderId = OrderId,
				PosData = PosData,
				CurrentTime = DateTimeOffset.UtcNow,
				InvoiceTime = InvoiceTime,
				ExpirationTime = ExpirationTime,
				BTCPrice = Money.Coins((decimal)(1.0 / Rate)).ToString(),
				Status = Status,
				Url = ServerUrl.WithTrailingSlash() +  "invoice?id=" + Id,
				Currency = ProductInformation.Currency,
				Flags = new Flags() { Refundable = Refundable }
			};
			Populate(ProductInformation, dto);
			Populate(BuyerInformation, dto);
			dto.ExRates = new Dictionary<string, double>
			{
				{ ProductInformation.Currency, Rate }
			};
			dto.PaymentUrls = new InvoicePaymentUrls()
			{
				BIP72 = $"bitcoin:{DepositAddress}?amount={GetCryptoDue()}&r={ServerUrl.WithTrailingSlash() + ($"i/{Id}")}",
				BIP72b = $"bitcoin:?r={ServerUrl.WithTrailingSlash() + ($"i/{Id}")}",
				BIP73 = ServerUrl.WithTrailingSlash() + ($"i/{Id}"),
				BIP21 = $"bitcoin:{DepositAddress}?amount={GetCryptoDue()}",
			};
			dto.BitcoinAddress = DepositAddress.ToString();
			dto.Token = Encoders.Base58.EncodeData(RandomUtils.GetBytes(16)); //No idea what it is useful for
			dto.Guid = Guid.NewGuid().ToString();

			var paid = Payments.Select(p => p.Output.Value).Sum();
			dto.BTCPaid = paid.ToString();
			dto.BTCDue = GetCryptoDue().ToString();

			dto.ExceptionStatus = ExceptionStatus == null ? new JValue(false) : new JValue(ExceptionStatus);
			return dto;
		}

		private void Populate<TFrom, TDest>(TFrom from, TDest dest)
		{
			var str = JsonConvert.SerializeObject(from);
			JsonConvert.PopulateObject(str, dest);
		}

		public Money GetNetworkFee()
		{
			var item = Calculate();
			return TxFee * item.TxCount;
		}
	}

	public class PaymentEntity
	{
		public DateTimeOffset ReceivedTime
		{
			get; set;
		}
		public OutPoint Outpoint
		{
			get; set;
		}
		public TxOut Output
		{
			get; set;
		}
	}
}
