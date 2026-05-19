#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Plugins.Wallets.Views.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Controllers;

public partial class UIStoresController
{

    [HttpGet("{storeId}/labels.json")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> StoreLabelsJson(
        string storeId,
        bool excludeTypes = true,
        string? linkedType = null)
    {
        if (string.IsNullOrEmpty(linkedType))
            return BadRequest("linkedType is required.");

        var labels = await _storeLabelRepository.GetStoreLabels(storeId, linkedType);

        return Ok(labels
            .Where(l => !excludeTypes || !WalletObjectData.Types.AllTypes.Contains(l.Label))
            .Select(l => new WalletLabelModel
            {
                Label = l.Label,
                Color = l.Color,
                TextColor = ColorPalette.Default.TextColor(l.Color)
            }));
    }

    public class UpdateStoreLabelsRequest
    {
        public string? Type { get; set; }
        public string? Id { get; set; }
        public string[]? Labels { get; set; }
    }

    [HttpPost("{storeId}/update-labels")]
    [Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateStoreLabels(string storeId, [FromBody] UpdateStoreLabelsRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Type) || string.IsNullOrWhiteSpace(request.Id))
            return BadRequest();

        await _storeLabelRepository.SetStoreObjectLabels(storeId, request.Type, request.Id, request.Labels ?? Array.Empty<string>());

        return Ok();
    }
}
