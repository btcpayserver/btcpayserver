#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Models.WalletViewModels;
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
        string? linkedType = null,
        string? type = null,
        string? id = null)
    {
        var store = CurrentStore;
        if (store is null || !string.Equals(store.Id, storeId, StringComparison.Ordinal))
            return NotFound();

        (string Label, string Color)[] labels;
        if (!string.IsNullOrEmpty(type) && !string.IsNullOrEmpty(id))
        {
            labels = await _storeLabelRepository.GetStoreLabels(storeId, type, id);
        }
        else if (!string.IsNullOrEmpty(linkedType))
        {
            labels = await _storeLabelRepository.GetStoreLabelsByLinkedType(storeId, linkedType);
        }
        else
        {
            labels = await _storeLabelRepository.GetStoreLabels(storeId);
        }

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
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> UpdateStoreLabels(string storeId, [FromBody] UpdateStoreLabelsRequest request)
    {
        var store = CurrentStore;
        if (store is null || !string.Equals(store.Id, storeId, StringComparison.Ordinal))
            return NotFound();

        if (request.Type is null || request.Id is null)
            return BadRequest();

        await _storeLabelRepository.SetStoreObjectLabels(storeId, request.Type, request.Id, request.Labels ?? Array.Empty<string>());

        return Ok();
    }
}
