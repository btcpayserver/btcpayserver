using System.Text.Json;
using BTCPayServer.Models;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace BTCPayServer
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
