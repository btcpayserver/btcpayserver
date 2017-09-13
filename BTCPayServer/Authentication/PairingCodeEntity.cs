using System;
using System.Collections.Generic;
using System.Text;

namespace BTCPayServer.Authentication
{
	public class PairingCodeEntity
	{
		public string Id
		{
			get;
			set;
		}
		public string Facade
		{
			get;
			set;
		}
		public string Label
		{
			get;
			set;
		}
		public string SIN
		{
			get;
			set;
		}
		public DateTimeOffset PairingTime
		{
			get;
			set;
		}
		public DateTimeOffset PairingExpiration
		{
			get;
			set;
		}
		public string Token
		{
			get;
			set;
		}

		public bool IsExpired()
		{
			return DateTimeOffset.UtcNow > PairingExpiration;
		}
	}
}
