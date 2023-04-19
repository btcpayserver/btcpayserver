using System;
using System.ComponentModel.DataAnnotations;
using System.Xml.Linq;
using BTCPayServer.Abstractions.Form;
using BTCPayServer.Validation;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Serilog;
using Serilog.Events;

namespace BTCPayServer.Services
{

    public class LogSettings
    {
        /*[Display(Name = "Send log by email")]
        public bool logEmailEnabled { get; set; }
        public LogEmailConfig emailConfig { get; set; }*/

        [Display(Name = "Send log to a Slack Channel")]
        public bool logSlackEnabled { get; set; }
        public LogSlackConfig slackConfig { get; set; }

        [Display(Name = "Send log to a Telegram Canal")]
        public bool logTelegramEnabled { get; set; }
        public LogTelegramConfig telegramConfig { get; set; }

        public LogSettings()
        {
            //emailConfig = new LogEmailConfig();
            telegramConfig = new LogTelegramConfig();
            slackConfig = new LogSlackConfig();
        }

    }

    public abstract class LogConfig
    {
        [Display(Name = "Notification minimum level : ")]
        public LogEventLevel MinLevel { get; set; } = LogEventLevel.Information;

        public abstract bool IsComplete();
    }

    /*public class LogEmailConfig : LogConfig
    {
        [Display(Name = "Destinary")]
        [MailboxAddressAttribute]
        public string To { get; set; }

        [Display(Name = "Nb Max of Events in each email")]
        [Range(1,100)]
        public int NbMaxEventsInMail { get; set; } = 10;

        [Display(Name = "Template")]
        public string Template { get; set; } = "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level}] {Message}{NewLine}{Exception}";

        [Display(Name = "Period (in second)")]
        [Range(1,20)]
        public int Period { get; set; } = 10;

        public TimeSpan PeriodTimeSpan { get { return new TimeSpan(Period * 10000000); } }

        public override bool IsComplete()
        {
            return MailboxAddressValidator.IsMailboxAddress(To)
                && !string.IsNullOrWhiteSpace(Template)
                && Period >= 1 && Period <= 20
                && NbMaxEventsInMail>=1 && NbMaxEventsInMail <= 100;
        }
    }*/


    public class LogSlackConfig : LogConfig
    {
        [Display(Name = "Channel")]
        public string Channel { get; set; }

        [Display(Name = "User Name")]
        public string UserName { get; set; }

        [Display(Name = "Hook Url")]
        public string HookUrl { get; set; }

        public override bool IsComplete()
        {
            return !string.IsNullOrWhiteSpace(Channel)
                && !string.IsNullOrWhiteSpace(UserName)
                && !string.IsNullOrWhiteSpace(HookUrl);
        }
    }

    public class LogTelegramConfig : LogConfig
    {
        [Display(Name = "Token")]
        public string Token { get; set; }

        [Display(Name = "Chat ID")]
        public string ChatID { get; set; }

        public override bool IsComplete()
        {
            return !string.IsNullOrWhiteSpace(Token)
                && !string.IsNullOrWhiteSpace(ChatID);
        }
    }
}
