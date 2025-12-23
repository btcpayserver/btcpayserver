using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Plugins.Emails.Views;

namespace BTCPayServer.Plugins.Emails.Migrations;

public class DefaultServerEmailRulesMigration(IEnumerable<EmailTriggerViewModel> vms) : MigrationBase<ApplicationDbContext>("20251109_defaultserverrules")
{
    public override Task MigrateAsync(ApplicationDbContext dbContext, CancellationToken cancellationToken)
    {
        var defaultRules = new string[] {
            ServerMailTriggers.PasswordReset,
            ServerMailTriggers.InvitePending,
            ServerMailTriggers.ApprovalConfirmed,
            ServerMailTriggers.ApprovalPending,
            ServerMailTriggers.EmailConfirm,
            ServerMailTriggers.ApprovalRequest,
            ServerMailTriggers.InviteConfirmed
        }.ToHashSet();
        foreach (var vm in vms.Where(v => defaultRules.Contains(v.Trigger)))
        {
            dbContext.EmailRules.Add(new()
            {
                To = vm.DefaultEmail.To,
                CC = vm.DefaultEmail.CC,
                BCC = vm.DefaultEmail.BCC,
                Trigger = vm.Trigger,
                Subject = vm.DefaultEmail.Subject,
                Body = vm.DefaultEmail.Body,
            });
        }
        return Task.CompletedTask;
    }
}
