
using BTCPayServer.Models;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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
            tempData["StatusMessageModel"] = JObject.FromObject(statusMessage).ToString(Formatting.None);
        }
    }
}
