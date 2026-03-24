#nullable enable
using System.Collections.Generic;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Models;
using BTCPayServer.Plugins.Bitpay.Models;
using BTCPayServer.Plugins.Bitpay.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Plugins.Bitpay.Controllers;

[Authorize(AuthenticationSchemes = AuthenticationSchemes.Bitpay)]
[BitpayEndpointSelectorPolicy.BitpayEndpointMetadata]
[BitpayFilter]
public class BitpayAccessTokenController(TokenRepository tokenRepository) : ControllerBase
{

    [HttpGet]
    [Route("tokens")]
    public async Task<GetTokensResponse> Tokens()
    {
        var tokens = await tokenRepository.GetTokens(this.User.GetSIN());
        return new GetTokensResponse(tokens);
    }

    [HttpPost]
    [Route("tokens")]
    [AllowAnonymous]
    public async Task<DataWrapper<List<PairingCodeResponse>>> Tokens([FromBody] TokenRequest request)
    {
        if (request == null)
            throw new BitpayHttpException(400, "The request body is missing");
        PairingCodeEntity? pairingEntity;
        if (string.IsNullOrEmpty(request.PairingCode))
        {
            if (string.IsNullOrEmpty(request.Id) || !NBitpayClient.Extensions.BitIdExtensions.ValidateSIN(request.Id))
                throw new BitpayHttpException(400, "'id' property is required");

            var pairingCode = await tokenRepository.CreatePairingCodeAsync();
            await tokenRepository.PairWithSINAsync(pairingCode, request.Id);
            pairingEntity = await tokenRepository.UpdatePairingCode(new PairingCodeEntity()
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

            pairingEntity = await tokenRepository.GetPairingAsync(request.PairingCode);
            if (pairingEntity == null)
                throw new BitpayHttpException(404, "The specified pairingCode is not found");
            pairingEntity.SIN = sin;

            if (string.IsNullOrEmpty(pairingEntity.Label) && !string.IsNullOrEmpty(request.Label))
            {
                pairingEntity.Label = request.Label;
                await tokenRepository.UpdatePairingCode(pairingEntity);
            }

            var result = await tokenRepository.PairWithSINAsync(request.PairingCode, sin);
            if (result != PairingResult.Complete && result != PairingResult.Partial)
                throw new BitpayHttpException(400, $"Error while pairing ({result})");
        }

        var pairingCodes = new List<PairingCodeResponse>
        {
            new()
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
