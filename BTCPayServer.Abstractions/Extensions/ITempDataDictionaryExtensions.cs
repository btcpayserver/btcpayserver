using System.Text.Json;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Models;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Abstractions.Extensions;

public static class SetStatusMessageModelExtensions
{
    public static void SetStatusSuccess(this ITempDataDictionary tempData, string statusMessage)
    {
        tempData.SetStatusMessageModel(new StatusMessageModel
        {
            Severity = StatusMessageModel.StatusSeverity.Success,
            Message = statusMessage
        });
    }
    public static void SetStatusMessageModel(this ITempDataDictionary tempData, StatusMessageModel statusMessage)
    {
        if (statusMessage == null)
        {
            tempData.Remove("StatusMessageModel");
            return;
        }

        tempData["StatusMessageModel"] = JsonSerializer.Serialize(statusMessage, new JsonSerializerOptions());
    }

    public static StatusMessageModel GetStatusMessageModel(this ITempDataDictionary tempData)
    {
        tempData.TryGetValue(WellKnownTempData.SuccessMessage, out var successMessage);
        tempData.TryGetValue(WellKnownTempData.ErrorMessage, out var errorMessage);
        tempData.TryGetValue("StatusMessageModel", out var model);
        if (successMessage != null || errorMessage != null)
        {
            var parsedModel = new StatusMessageModel
            {
                Message = (string)successMessage ?? (string)errorMessage,
                Severity = successMessage != null ? StatusMessageModel.StatusSeverity.Success : StatusMessageModel.StatusSeverity.Error
            };
            return parsedModel;
        }
        if (model is string str)
        {
            return JObject.Parse(str).ToObject<StatusMessageModel>();
        }
        return null;
    }

    public static bool HasStatusMessage(this ITempDataDictionary tempData)
    {
        return (tempData.Peek(WellKnownTempData.SuccessMessage) ??
                tempData.Peek(WellKnownTempData.ErrorMessage) ??
                tempData.Peek("StatusMessageModel")) != null;
    }

    public static bool HasErrorMessage(this ITempDataDictionary tempData)
    {
        return GetStatusMessageModel(tempData)?.Severity == StatusMessageModel.StatusSeverity.Error;
    }
}
