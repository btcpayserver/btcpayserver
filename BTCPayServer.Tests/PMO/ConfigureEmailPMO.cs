#nullable enable
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Models;

namespace BTCPayServer.Tests.PMO;

public class ConfigureEmailPMO(PlaywrightTester s)
{
    public class Form
    {
        public string? Server { get; set; }
        public int? Port { get; set; }
        public string? From { get; set; }
        public string? Login { get; set; }
        public string? Password { get; set; }
        public bool? EnabledCertificateCheck { get; set; }
    }

    public async Task<EmailRulesPMO> ConfigureEmailRules()
    {
        await s.Page.ClickAsync("#ConfigureEmailRules");
        return new EmailRulesPMO(s);
    }

    public Task FillMailPit(Form? form = null)
        => Fill(new()
        {
            Server =  s.Server.MailPitSettings.Hostname,
            Port = s.Server.MailPitSettings.SmtpPort,
            From = form?.From ?? "from@example.com",
            Login = form?.Login ?? "login@example.com",
            Password = form?.Password ?? "password",
            EnabledCertificateCheck = false,
        });

    public async Task Fill(Form form)
    {
        if (form.Server is not null)
            await s.Page.FillAsync("#Settings_Server", form.Server);
        if (form.Port is { } p)
            await s.Page.FillAsync("#Settings_Port", p.ToString());
        if (form.From is not null)
            await s.Page.FillAsync("#Settings_From", form.From);
        if (form.Login is not null)
            await s.Page.FillAsync("#Settings_Login", form.Login);
        if (form.Password is not null)
            await s.Page.FillAsync("#Settings_Password", form.Password);
        if (form.EnabledCertificateCheck is { } v)
        {
            await s.Page.ClickAsync("#AdvancedSettingsButton");
            await s.Page.SetCheckedAsync("#Settings_EnabledCertificateCheck", v);
        }
        await s.ClickPagePrimary();
    }
}
