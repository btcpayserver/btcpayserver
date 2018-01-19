using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using BTCPayServer.Authentication;
using NBitcoin.DataEncoders;
using Microsoft.AspNetCore.Http;
using System.IO;

namespace BTCPayServer.Models
{
    //{"data":[{"pos":"FfZ6WCa8TunAvPCpQZXkdBsoH4Yo18FyPaJ5X5qjrVVY"},{"pos/invoice":"H1pwwh2tMeSCri9rh5VvHWEHokGdf2EGtghfZkUEbeZv"},{"merchant":"89zEBr9orAc6wgybAABp8ioGcjYeFrUaZgMzjxNuqYty"},{"merchant/invoice":"8e7ijDxGfJsWXWgJuKXjjNgxnX1xpsBM8cTZCFnU7ehj"}]}
    public class GetTokensResponse : IActionResult
    {
        BitTokenEntity[] _Tokens;
        public GetTokensResponse(BitTokenEntity[] tokens)
        {
            if (tokens == null)
                throw new ArgumentNullException(nameof(tokens));
            this._Tokens = tokens;
        }

        [JsonProperty(PropertyName = "data")]
        //{"pos":"FfZ6WCa8TunAvPCpQZXkdBsoH4Yo18FyPaJ5X5qjrVVY"}
        public JArray Data
        {
            get; set;
        }

        public async Task ExecuteResultAsync(ActionContext context)
        {
            JObject jobj = new JObject();
            JArray jarray = new JArray();
            jobj.Add("data", jarray);
            foreach (var token in _Tokens)
            {
                JObject item = new JObject();
                jarray.Add(item);
                JProperty jProp = new JProperty(token.Facade);
                item.Add(jProp);
                jProp.Value = token.Value;
            }
            context.HttpContext.Response.Headers.Add("Content-Type", new Microsoft.Extensions.Primitives.StringValues("application/json"));
            var str = JsonConvert.SerializeObject(jobj);
            using (var writer = new StreamWriter(context.HttpContext.Response.Body, new UTF8Encoding(false), 1024 * 10, true))
            {
                await writer.WriteAsync(str);
            }
        }
    }
}
