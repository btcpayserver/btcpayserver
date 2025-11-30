using System;
using System.IO;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Mvc.Localization;
using Microsoft.Extensions.Localization;
using Newtonsoft.Json;

namespace BTCPayServer.Abstractions.Models
{
    public class StatusMessageModel
    {
        public StatusMessageModel()
        {
        }
        public string Message { get; set; }

        [JsonIgnore]
        public LocalizedString LocalizedMessage
        {
            set
            {
                Message = value.Value;
            }
        }

        public string Html { get; set; }
        [JsonIgnore]
        public  LocalizedHtmlString LocalizedHtml
        {
            set
            {
                StringWriter w = new();
                value.WriteTo(w, HtmlEncoder.Default);
                Html = w.ToString();
            }
        }
        public StatusSeverity Severity { get; set; }
        public bool AllowDismiss { get; set; } = true;

        public string SeverityCSS => ToString(Severity);

        public static string ToString(StatusSeverity severity)
        {
            switch (severity)
            {
                case StatusSeverity.Info:
                    return "info";
                case StatusSeverity.Error:
                    return "danger";
                case StatusSeverity.Success:
                    return "success";
                case StatusSeverity.Warning:
                    return "warning";
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public enum StatusSeverity
        {
            Info,
            Error,
            Success,
            Warning
        }
    }
}
