using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using BTCPayServer.Data;

namespace BTCPayServer.Plugins.Emails.Views;

public class StoreEmailRuleViewModel
{
    public StoreEmailRuleViewModel()
    {

    }
    public StoreEmailRuleViewModel(EmailRuleData data, IEnumerable<EmailTriggerViewModel> triggers)
    {
        if (data is not null)
        {
            Data = data;
            AdditionalData = data.GetBTCPayAdditionalData() ?? new();
            Trigger = data.Trigger;
            Subject = data.Subject;
            Condition = data.Condition ?? "";
            Body = data.Body;
            To = string.Join(",", data.To);
        }
        else
        {
            AdditionalData = new();
        }

        Triggers = triggers.ToList();
    }
    [Required]
    public string Trigger { get; set; }

    public string Condition { get; set; }

    [Required]
    public string Subject { get; set; }

    [Required]
    public string Body { get; set; }
    public EmailRuleData Data { get; set; }
    public EmailRuleData.BTCPayAdditionalData AdditionalData { get; set; }
    public string To { get; set; }

    public List<EmailTriggerViewModel> Triggers { get; set; }
    public string[] ToAsArray()
    => (To ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim())
            .ToArray();
}
