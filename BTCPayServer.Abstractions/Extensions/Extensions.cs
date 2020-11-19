using System.Text.Json;
using BTCPayServer.Abstractions.Models;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace BTCPayServer.Abstractions.Extensions
{
    public static class SetStatusMessageModelExtensions
    {
        public static void SetStatusMessageModel(this ITempDataDictionary tempData, StatusMessageModel statusMessage)
        {
            if (statusMessage == null)
            {
                tempData.Remove("StatusMessageModel");
                return;
            }

            tempData["StatusMessageModel"] = JsonSerializer.Serialize(statusMessage, new JsonSerializerOptions());
        }
    }
}
