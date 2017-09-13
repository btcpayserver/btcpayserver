using BTCPayServer.Authentication;
using BTCPayServer.Filters;
using BTCPayServer.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NBitcoin.DataEncoders;
using NBitpayClient;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace BTCPayServer.Controllers
{
	public class AccessTokenController : Controller
	{
		TokenRepository _TokenRepository;
		public AccessTokenController(TokenRepository tokenRepository)
		{
			_TokenRepository = tokenRepository ?? throw new ArgumentNullException(nameof(tokenRepository));
		}
		[HttpGet]
		[Route("tokens")]
		public async Task<GetTokensResponse> GetTokens()
		{
			var tokens = await _TokenRepository.GetTokens(this.GetBitIdentity().SIN);
			return new GetTokensResponse(tokens);
		}

		[HttpPost]
		[Route("tokens")]
		public async Task<DataWrapper<List<PairingCodeResponse>>> GetPairingCode([FromBody] PairingCodeRequest token)
		{
			var now = DateTimeOffset.UtcNow;
			var pairingEntity = new PairingCodeEntity()
			{
				Facade = token.Facade,
				Label = token.Label,
				SIN = token.Id,
				PairingTime = now,
				PairingExpiration = now + TimeSpan.FromMinutes(15)
			};
			var grantedToken = await _TokenRepository.CreateToken(token.Id, token.Facade);
			pairingEntity.Token = grantedToken.Name;
			pairingEntity = await _TokenRepository.AddPairingCodeAsync(pairingEntity);

			var pairingCodes = new List<PairingCodeResponse>
			{
				new PairingCodeResponse()
				{
					PairingCode = pairingEntity.Id,
					PairingExpiration = pairingEntity.PairingExpiration,
					DateCreated = pairingEntity.PairingTime,
					Facade = grantedToken.Name,
					Token = grantedToken.Value,
					Label = pairingEntity.Label
				}
			};
			return DataWrapper.Create(pairingCodes);
		}
	}
}
