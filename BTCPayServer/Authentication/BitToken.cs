using NBitcoin;
using System;
using System.Collections.Generic;
using System.Text;
using NBitpayClient;

namespace BTCPayServer.Authentication
{
    public class BitTokenEntity
    {
		public string Name
		{
			get; set;
		}
		public string Value
		{
			get; set;
		}
		public DateTimeOffset DateCreated
		{
			get; set;
		}
		public bool Active
		{
			get; set;
		}
		public string PairedId
		{
			get; set;
		}
		public string Label
		{
			get; set;
		}
		public DateTimeOffset PairingTime
		{
			get; set;
		}
		public string SIN
		{
			get;
			set;
		}

		public BitTokenEntity Clone(Facade facade)
		{
			return new BitTokenEntity()
			{
				Active = Active,
				DateCreated = DateCreated,
				Label = Label,
				Name = Name,
				PairedId = PairedId,
				PairingTime = PairingTime,
				SIN = SIN,
				Value = Value
			};
		}
	}
}
