using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Microsoft.EntityFrameworkCore;
using BTCPayServer.Data;

namespace BTCPayServer.Plugins.Emails.Services;

public class EmailLogRepository(ApplicationDbContextFactory dbContextFactory)
{
    public const int RetentionDays = 10;

    public async Task Add(EmailLogEntry log)
    {
        await using var ctx = dbContextFactory.CreateContext();
        await ctx.Database.GetDbConnection().ExecuteAsync(
            """
            INSERT INTO email_logs (id, store_id, status, trigger, "to", cc, bcc, subject, body, error)
            VALUES (@Id, @StoreId, @Status, @Trigger, @To, @CC, @BCC, @Subject, @Body, @Error)
            """,
            new
            {
                log.Id,
                log.StoreId,
                Status = log.Status.ToString(),
                log.Trigger,
                log.To,
                log.CC,
                log.BCC,
                log.Subject,
                log.Body,
                log.Error
            });
    }

    public async Task<(EmailLogListItem[] Items, int Total)> GetPage(string? storeId, int skip, int count)
    {
        var where = storeId is null ? "store_id IS NULL" : "store_id = @storeId";
        await using var ctx = dbContextFactory.CreateContext();
        var conn = ctx.Database.GetDbConnection();
        var total = await conn.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM email_logs WHERE {where}", new { storeId });
        var items = await conn.QueryAsync<EmailLogListItem>(
            $"""
             SELECT id AS Id, created_at AS Timestamp, status AS Status, trigger AS Trigger, "to" AS "To", subject AS Subject, error AS Error
             FROM email_logs
             WHERE {where}
             ORDER BY created_at DESC, id
             LIMIT @count OFFSET @skip
             """,
            new { storeId, skip, count });
        return (items.ToArray(), total);
    }

    public async Task<EmailLogEntry[]> GetByIds(string? storeId, string[] ids)
    {
        var where = storeId is null ? "store_id IS NULL" : "store_id = @storeId";
        await using var ctx = dbContextFactory.CreateContext();
        var rows = await ctx.Database.GetDbConnection().QueryAsync<EmailLogEntry>(
            $"""
             SELECT id AS Id, store_id AS StoreId, created_at AS Timestamp, status AS Status, trigger AS Trigger,
                    "to" AS "To", cc AS CC, bcc AS BCC, subject AS Subject, body AS Body, error AS Error
             FROM email_logs
             WHERE {where} AND id = ANY(@ids)
             """,
            new { storeId, ids });
        return rows.ToArray();
    }
}
