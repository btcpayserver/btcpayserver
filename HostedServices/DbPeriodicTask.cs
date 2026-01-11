using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Data;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.HostedServices;

public class DbPeriodicTask(
    ILogger<DbPeriodicTask> logger,
    IEnumerable<DbPeriodicTask.PeriodicScript> scripts,
    ApplicationDbContextFactory dbContextFactory) : IPeriodicTask
{
    public record PeriodicScript(string Name, string Script);

    public DateTimeOffset? Now { get; set; }

    public async Task Do(CancellationToken cancellationToken)
    {
        await using var ctx = dbContextFactory.CreateContext(o => o.CommandTimeout((int)TimeSpan.FromMinutes(5).TotalSeconds));
        var conn = ctx.Database.GetDbConnection();
        foreach (var script in scripts)
        {
            await RunScript(script, conn, cancellationToken);
        }
    }

    public async Task<int> RunScript(string scriptName)
    {
        var s = scripts.First(s => s.Name == scriptName);
        await using var ctx = dbContextFactory.CreateContext();
        return await RunScript(s, ctx.Database.GetDbConnection());
    }

    async Task<int> RunScript(PeriodicScript script, DbConnection conn, CancellationToken cancellationToken = default)
    {
        logger.LogInformation($"Executing {script.Name}");
        DynamicParameters parameters = new();
        parameters.Add("now", Now ?? DateTimeOffset.UtcNow);
        var command = new CommandDefinition(script.Script, parameters, cancellationToken: cancellationToken);
        var count = await conn.ExecuteScalarAsync<int>(command);
        logger.LogInformation($"Executed {script.Name} (Returned: {count})");
        return count;
    }
}
