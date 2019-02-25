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
            if (string.IsNullOrEmpty(s))
                return;
            try
            {
                if (s.StartsWith("{", StringComparison.InvariantCultureIgnoreCase) &&
                    s.EndsWith("}", StringComparison.InvariantCultureIgnoreCase))
                {
                    var model = JObject.Parse(s).ToObject<StatusMessageModel>();
                    Html = model.Html;
                    Message = model.Message;
                    Severity = model.Severity;
                }
                else
                {
                    ParseNonJsonStatus(s);
                }
            }
            catch (Exception)
            {
                ParseNonJsonStatus(s);
            }
        }

        public string Message { get; set; }
        public string Html { get; set; }
        public StatusSeverity Severity { get; set; }

        public string SeverityCSS
        {
            get
            {
                switch (Severity)
                {
                    case StatusSeverity.Info:
                        return "info";
                    case StatusSeverity.Error:
                        return "danger";
                    case StatusSeverity.Success:
                        return "success";
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }

        public override string ToString()
        {
            return JObject.FromObject(this).ToString(Formatting.None);
        }
        
        private void ParseNonJsonStatus(string s)
        {
            Message = s;
            Severity = s.StartsWith("Error", StringComparison.InvariantCultureIgnoreCase)
                ? StatusSeverity.Error
                : StatusSeverity.Success;
        }

        public enum StatusSeverity
        {
            Info,
            Error,
            Success
        }
    }
}
