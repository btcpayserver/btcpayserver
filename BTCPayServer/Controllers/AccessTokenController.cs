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
		public async Task<GetTokensResponse> Tokens()
		{
			var tokens = await _TokenRepository.GetTokens(this.GetBitIdentity().SIN);
			return new GetTokensResponse(tokens);
		}

		[HttpPost]
		[Route("tokens")]
		public async Task<DataWrapper<List<PairingCodeResponse>>> Tokens([FromBody] TokenRequest request)
		{
			PairingCodeEntity pairingEntity = null;
			if(string.IsNullOrEmpty(request.PairingCode))
			{
				if(string.IsNullOrEmpty(request.Id) || !NBitpayClient.Extensions.BitIdExtensions.ValidateSIN(request.Id))
					throw new BitpayHttpException(400, "'id' property is required");
				if(string.IsNullOrEmpty(request.Facade))
					throw new BitpayHttpException(400, "'facade' property is required");

				var pairingCode = await _TokenRepository.CreatePairingCodeAsync();
				await _TokenRepository.PairWithSINAsync(pairingCode, request.Id);
				pairingEntity = await _TokenRepository.UpdatePairingCode(new PairingCodeEntity()
				{
					Id = pairingCode,
					Facade = request.Facade,
					Label = request.Label
				});

			}
			else
			{
				var sin = this.GetBitIdentity(false)?.SIN ?? request.Id;
				if(string.IsNullOrEmpty(request.Id) || !NBitpayClient.Extensions.BitIdExtensions.ValidateSIN(request.Id))
					throw new BitpayHttpException(400, "'id' property is required, alternatively, use BitId");

				pairingEntity = await _TokenRepository.GetPairingAsync(request.PairingCode);
				pairingEntity.SIN = sin;
				if(!await _TokenRepository.PairWithSINAsync(request.PairingCode, sin))
					throw new BitpayHttpException(400, "Unknown pairing code");
				
			}

			var pairingCodes = new List<PairingCodeResponse>
				{
					new PairingCodeResponse()
					{
						PairingCode = pairingEntity.Id,
						PairingExpiration = pairingEntity.Expiration,
						DateCreated = pairingEntity.CreatedTime,
						Facade = pairingEntity.Facade,
						Token = pairingEntity.TokenValue,
						Label = pairingEntity.Label
					}
				};
			return DataWrapper.Create(pairingCodes);
		}
	}
}
