using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Filters;
using BTCPayServer.Models;
using BTCPayServer.Security.Bitpay;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Controllers
{
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Bitpay)]
    [BitpayAPIConstraint()]
    public class BitpayAccessTokenController : Controller
    {
        readonly TokenRepository _TokenRepository;
        public BitpayAccessTokenController(TokenRepository tokenRepository)
        {
            _TokenRepository = tokenRepository ?? throw new ArgumentNullException(nameof(tokenRepository));
        }
        [HttpGet]
        [Route("tokens")]
        public async Task<GetTokensResponse> Tokens()
        {
            var tokens = await _TokenRepository.GetTokens(this.User.GetSIN());
            return new GetTokensResponse(tokens);
        }

        [HttpPost]
        [Route("tokens")]
        [AllowAnonymous]
        public async Task<DataWrapper<List<PairingCodeResponse>>> Tokens([FromBody] TokenRequest request)
        {
            if (request == null)
                throw new BitpayHttpException(400, "The request body is missing");
            PairingCodeEntity pairingEntity = null;
            if (string.IsNullOrEmpty(request.PairingCode))
            {
                if (string.IsNullOrEmpty(request.Id) || !NBitpayClient.Extensions.BitIdExtensions.ValidateSIN(request.Id))
                    throw new BitpayHttpException(400, "'id' property is required");

                var pairingCode = await _TokenRepository.CreatePairingCodeAsync();
                await _TokenRepository.PairWithSINAsync(pairingCode, request.Id);
                pairingEntity = await _TokenRepository.UpdatePairingCode(new PairingCodeEntity()
                {
                    Id = pairingCode,
                    Label = request.Label
                });

            }
            else
            {
                var sin = this.User.GetSIN() ?? request.Id;
                if (string.IsNullOrEmpty(sin) || !NBitpayClient.Extensions.BitIdExtensions.ValidateSIN(sin))
                    throw new BitpayHttpException(400, "'id' property is required, alternatively, use BitId");

                pairingEntity = await _TokenRepository.GetPairingAsync(request.PairingCode);
                if (pairingEntity == null)
                    throw new BitpayHttpException(404, "The specified pairingCode is not found");
                pairingEntity.SIN = sin;

                if (string.IsNullOrEmpty(pairingEntity.Label) && !string.IsNullOrEmpty(request.Label))
                {
                    pairingEntity.Label = request.Label;
                    await _TokenRepository.UpdatePairingCode(pairingEntity);
                }

                var result = await _TokenRepository.PairWithSINAsync(request.PairingCode, sin);
                if (result != PairingResult.Complete && result != PairingResult.Partial)
                    throw new BitpayHttpException(400, $"Error while pairing ({result})");

            }

            var pairingCodes = new List<PairingCodeResponse>
                {
                    new PairingCodeResponse()
                    {
                        Policies = new Newtonsoft.Json.Linq.JArray(),
                        PairingCode = pairingEntity.Id,
                        PairingExpiration = pairingEntity.Expiration,
                        DateCreated = pairingEntity.CreatedTime,
                        Facade = "merchant",
                        Token = pairingEntity.TokenValue,
                        Label = pairingEntity.Label
                    }
                };
            return DataWrapper.Create(pairingCodes);
        }
    }
}
