using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Models
{
    public class StatusMessageModel
    {
        public StatusMessageModel()
        {
                
        }
        public StatusMessageModel(string s)
        {
            try
            {
                var model = JObject.Parse(s).ToObject<StatusMessageModel>();
                Html = model.Html;
                Message = model.Message;
                Severity = model.Severity;
            }
            catch (Exception e)
            {
                Message = s;
                Severity = s.StartsWith("Error", StringComparison.InvariantCultureIgnoreCase) ? "danger" : "success";
            }
        }
        public string Message { get; set; }
        public string Html { get; set; }
        public string Severity { get; set; }

        public override string ToString()
        {
            return JObject.FromObject(this).ToString(Formatting.None);
        }
    }
}
