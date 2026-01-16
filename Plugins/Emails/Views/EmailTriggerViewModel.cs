using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace BTCPayServer.Plugins.Emails.Views;

/// <summary>
/// This view model is used in StoreEmailRulesManage.cshtml, to display the different triggers that can be used to send emails
/// </summary>
public class EmailTriggerViewModel
{
    public class Default
    {
        public string Subject { get; set; }
        public string Body { get; set; }
        public string[] To { get; set; } = Array.Empty<string>();
        [JsonProperty("cc")]
        public string[] CC { get; set; } = Array.Empty<string>();
        [JsonProperty("bcc")]
        public string[] BCC { get; set; } = Array.Empty<string>();
        public bool CanIncludeCustomerEmail { get; set; }
    }

    public string Trigger { get; set; }

    public string Description { get; set; }

    public Default DefaultEmail { get; set; }

    public class PlaceHolder(string name, string description)
    {
        public string Name { get; set; } = name;
        public string Description { get; set; } = description;
    }

    public List<PlaceHolder> PlaceHolders { get; set; } = new();
    public bool ServerTrigger { get; set; }

    public EmailTriggerViewModel Clone()
    {
        var json = JsonConvert.SerializeObject(this);
        return JsonConvert.DeserializeObject<EmailTriggerViewModel>(json);
    }
}
