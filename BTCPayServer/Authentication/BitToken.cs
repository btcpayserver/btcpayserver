using NBitcoin;
using System;
using System.Collections.Generic;
using System.Text;

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
	}
}
