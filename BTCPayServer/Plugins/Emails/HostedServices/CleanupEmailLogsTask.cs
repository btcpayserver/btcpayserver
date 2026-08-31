using System;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.HostedServices;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Plugins.Emails.HostedServices;

public class CleanupEmailLogsTask(ApplicationDbContextFactory dbContextFactory) : IPeriodicTask
{
    public TimeSpan PruneAfter { get; set; } = TimeSpan.FromDays(10);

    public async Task Do(CancellationToken cancellationToken)
    {
        await using var ctx = dbContextFactory.CreateContext();
        var cutoff = DateTimeOffset.UtcNow - PruneAfter;
        await ctx.EmailLogs.Where(l => l.Timestamp < cutoff).ExecuteDeleteAsync(cancellationToken);
    }
}
