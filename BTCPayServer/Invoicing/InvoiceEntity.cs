using NBitcoin;
using System.Linq;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace BTCPayServer.Invoicing
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
		public string OrderId
		{
			get; set;
		}

		public Money GetTotalCryptoDue()
		{
			return Calculate().TotalDue;
		}

		private (Money TotalDue, Money Paid) Calculate()
		{
			var totalDue = Money.Coins((decimal)(ProductInformation.Price / Rate)) + TxFee;
			var paid = Money.Zero;
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
						totalDue += TxFee;
					return !paidEnough;
				})
				.ToArray();
			return (totalDue, paid);
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

		public bool IsExpired()
		{
			return DateTimeOffset.UtcNow > ExpirationTime;
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
