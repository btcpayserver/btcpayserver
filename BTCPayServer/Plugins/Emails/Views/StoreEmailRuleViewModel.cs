using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Net.Mail;
using BTCPayServer.Data;
using Newtonsoft.Json.Linq;

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
            StoreId = data.StoreId;
            Data = data;
            OfferingId = data.OfferingId;
            AdditionalData = data.GetBTCPayAdditionalData() ?? new();
            Trigger = data.Trigger;
            Subject = data.Subject;
            Condition = data.Condition ?? "";
            Body = data.Body;
            To = string.Join(",", data.To);
            CC = string.Join(",", data.CC);
            BCC = string.Join(",", data.BCC);
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
    public string CC { get; set; }
    public string BCC { get; set; }

    public List<EmailTriggerViewModel> Triggers { get; set; }
    public string RedirectUrl { get; set; }
    public bool CanChangeTrigger { get; set; } = true;
    public bool CanChangeCondition { get; set; } = true;
    public string OfferingId { get; set; }
    public string StoreId { get; set; }
    public bool IsNew { get; set; }
    public string[] AsArray(string values)
    {
        // This replace the placeholders with random email addresses
        // We can't just split input by comma, because display names of people can contain commas.
        // "John, Jr. Smith" <jon@example.com>,{User.Email},"Nicolas D." <nico@example.com>
        values ??= "";

        // We replace the placeholders with dummy email addresses
        var template = new TextTemplate(values);
        var dummy = $"{Random.Shared.Next()}@example.com";
        template.NotFoundReplacement = o => $"\"{o}\" <{dummy}>";
        values = template.Render(new JObject());
        if (string.IsNullOrWhiteSpace(values))
            return Array.Empty<string>();
        // We use MailAddressCollection to parse the addresses
        MailAddressCollection mailCollection = new();
        mailCollection.Add(values);
        foreach (var mail in  mailCollection)
        {
            // Let it throw if the address is invalid
            MailboxAddressValidator.Parse(mail.ToString());
        }
        // We replace the dummies with the former placeholders
        return mailCollection.Select(a => a.Address == dummy ? $"{{{a.DisplayName}}}" : a.ToString()).ToArray();
    }
}
