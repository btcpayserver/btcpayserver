#nullable enable
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.NTag424;
using Dapper;
using Microsoft.EntityFrameworkCore;
using NBitcoin.DataEncoders;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer;
public static class BoltcardDataExtensions
{
    public static async Task<int> LinkBoltcardToPullPayment(this ApplicationDbContextFactory dbContextFactory, string pullPaymentId, IssuerKey issuerKey, byte[] uid, OnExistingBehavior? onExisting = null)
    {
        onExisting ??= OnExistingBehavior.UpdateVersion;
        using var ctx = dbContextFactory.CreateContext();
        var conn = ctx.Database.GetDbConnection();

        string onConflict = onExisting switch
        {
            OnExistingBehavior.KeepVersion => "UPDATE SET ppid=excluded.ppid, version=boltcards.version",
            OnExistingBehavior.UpdateVersion => "UPDATE SET ppid=excluded.ppid, version=boltcards.version+1",
            _ => throw new NotSupportedException()
        };
        return await conn.QueryFirstOrDefaultAsync<int>(
        $"INSERT INTO boltcards(id, ppid) VALUES (@id, @ppid) ON CONFLICT (id) DO {onConflict} RETURNING version", new
        {
            id = GetId(issuerKey, uid),
            ppid = pullPaymentId
        });
    }
    public static async Task SetBoltcardResetState(this ApplicationDbContextFactory dbContextFactory, IssuerKey issuerKey, byte[] uid)
    {
        using var ctx = dbContextFactory.CreateContext();
        var conn = ctx.Database.GetDbConnection();
        await conn.ExecuteAsync("UPDATE boltcards SET ppid=NULL, counter=0 WHERE id=@id", new
        {
            id = GetId(issuerKey, uid)
        });
    }

    static string GetId(IssuerKey issuerKey, byte[] uid) => Encoders.Hex.EncodeData(issuerKey.GetId(uid));
    public record BoltcardRegistration(string? PullPaymentId, string Id, byte[] UId, int Version, int Counter);
    public static async Task<BoltcardRegistration?> GetBoltcardRegistration(this ApplicationDbContextFactory dbContextFactory, IssuerKey issuerKey, BoltcardPICCData piccData, bool updateCounter)
    {
        using var ctx = dbContextFactory.CreateContext();
        var conn = ctx.Database.GetDbConnection();
        var query = updateCounter ? "UPDATE boltcards SET counter=@counter WHERE id=@id AND counter < @counter RETURNING ppid, id, version, counter"
                                  : "SELECT ppid, id, version, counter FROM boltcards WHERE id=@id AND counter < @counter";
        var res = await conn.QueryFirstOrDefaultAsync(query, new { id = GetId(issuerKey, piccData.Uid), counter = piccData.Counter });
        if (res is null)
            return null;
        return new BoltcardRegistration(res.ppid, res.id, piccData.Uid, res.version, res.counter);
    }
    public static Task<BoltcardRegistration?> GetBoltcardRegistration(this ApplicationDbContextFactory dbContextFactory, IssuerKey issuerKey, byte[] uid)
    {
        var data = new BoltcardPICCData(uid, int.MaxValue);
        return GetBoltcardRegistration(dbContextFactory, issuerKey, data, false);
    }
}
