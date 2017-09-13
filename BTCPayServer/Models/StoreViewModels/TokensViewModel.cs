using BTCPayServer.Validations;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Models.StoreViewModels
{
	public class CreateTokenViewModel
	{
		[PubKeyValidatorAttribute]
		public string PublicKey
		{
			get; set;
		}
		[Required]
		public string Label
		{
			get; set;
		}

		[Required]
		public string Facade
		{
			get; set;
		}
	}
	public class TokenViewModel
	{
		public string Id
		{
			get; set;
		}
		public string Label
		{
			get; set;
		}
		public string SIN
		{
			get; set;
		}
		public string Facade
		{
			get; set;
		}
	}
	public class TokensViewModel
    {
		public TokenViewModel[] Tokens
		{
			get; set;
		}
		public string StatusMessage
		{
			get;
			set;
		}
	}
}
