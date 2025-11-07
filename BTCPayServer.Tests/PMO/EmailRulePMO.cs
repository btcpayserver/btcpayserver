#nullable  enable
using System.Threading.Tasks;

namespace BTCPayServer.Tests.PMO;

public class EmailRulesPMO(PlaywrightTester s)
{
    public async Task<EmailRulePMO> CreateEmailRule()
    {
        await s.Page.ClickAsync("#CreateEmailRule");
        return new EmailRulePMO(s);
    }
}

public  class EmailRulePMO(PlaywrightTester s)
{
    public class Form
    {
        public string? Trigger { get; set; }
        public string? To { get; set; }
        public string? Subject { get; set; }
        public string? Body { get; set; }
        public bool? CustomerEmail { get; set; }
        public string? Condition { get; set; }
        public bool HtmlBody { get; set; }
    }

    public async Task Fill(Form form)
    {
        if (form.Trigger is not null)
            await s.Page.SelectOptionAsync("#Trigger", form.Trigger);
        if (form.Condition is not null)
            await s.Page.FillAsync("#Condition", form.Condition);
        if (form.To is not null)
            await s.Page.FillAsync("#To", form.To);
        if (form.Subject is not null)
            await s.Page.FillAsync("#Subject", form.Subject);
        if (form.Body is not null)
        {
            if (form.HtmlBody)
            {
                await s.Page.ClickAsync(".btn-codeview");
                await s.Page.FillAsync(".note-codable", form.Body);
            }
            else
            {
                await s.Page.FillAsync(".note-editable", form.Body);
            }
        }

        if (form.CustomerEmail is {} v)
            await s.Page.SetCheckedAsync("#AdditionalData_CustomerEmail", v);
        await s.ClickPagePrimary();
    }
}
