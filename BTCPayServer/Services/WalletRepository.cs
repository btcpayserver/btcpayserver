using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Services.Wallets;
using Microsoft.EntityFrameworkCore;
using NBitcoin;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Services
{
#nullable enable
    public record WalletObjectId(WalletId WalletId, string Type, string Id);
    public record ObjectTypeId(string Type, string Id);
    public class GetWalletObjectsQuery
    {
        public GetWalletObjectsQuery()
        {

        }
        public GetWalletObjectsQuery(WalletId? walletId) : this(walletId, null, null)
        {
        }
        public GetWalletObjectsQuery(WalletObjectId walletObjectId) : this(walletObjectId.WalletId, walletObjectId.Type, new[] { walletObjectId.Id })
        {

        }
        public GetWalletObjectsQuery(WalletId? walletId, string type) : this(walletId, type, null)
        {
        }
        public GetWalletObjectsQuery(WalletId? walletId, string? type, string[]? ids)
        {
            WalletId = walletId;
            Type = type;
            Ids = ids;
        }
        public GetWalletObjectsQuery(WalletId? walletId,ObjectTypeId[]? typesIds)
        {
            WalletId = walletId;
            TypesIds = typesIds;
        }

        public WalletId? WalletId { get; set; }
        // Either the user passes a list of Types/Ids
        public ObjectTypeId[]? TypesIds { get; set; }
        // Or the user passes one type, and a list of Ids
        public string? Type { get; set; }
        public string[]? Ids { get; set; }
        public bool IncludeNeighbours { get; set; } = true;
        public bool UseInefficientPath { get; set; }
        
        public static ObjectTypeId Get(Script script)
        {
            return new ObjectTypeId(WalletObjectData.Types.Script, script.ToHex());
        }

        public static IEnumerable<ObjectTypeId> Get(ReceivedCoin coin)
        {
            yield return new ObjectTypeId(WalletObjectData.Types.Tx, coin.OutPoint.Hash.ToString());
            yield return Get(coin.ScriptPubKey);
            yield return new ObjectTypeId(WalletObjectData.Types.Utxo, coin.OutPoint.ToString());
        }
    }

#nullable restore
    public class WalletRepository
    {
        private readonly ApplicationDbContextFactory _ContextFactory;

        public WalletRepository(ApplicationDbContextFactory contextFactory)
        {
            _ContextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        }
#nullable enable
        public async Task<WalletObjectData?> GetWalletObject(WalletObjectId walletObjectId, bool includeNeighbours = true)
        {
            var r = await GetWalletObjects(new(walletObjectId) { IncludeNeighbours = includeNeighbours });
            return r.Select(o => o.Value).FirstOrDefault();
        }
        public async Task<Dictionary<WalletObjectId, WalletObjectData>> GetWalletObjects(GetWalletObjectsQuery queryObject)
        {
            ArgumentNullException.ThrowIfNull(queryObject);
            if (queryObject.Ids != null && queryObject.Type is null)
                throw new ArgumentException("If \"Ids\" is not null, \"Type\" is mandatory");
            if (queryObject.Type is not null && queryObject.TypesIds is not null)
                throw new ArgumentException("If \"Type\" is not null, \"TypesIds\" should be null");


            using var ctx = _ContextFactory.CreateContext();

            // If we are using postgres, the `transactionIds.Contains(w.BId)` result in a long query like `ANY(@txId1, @txId2, @txId3, @txId4)`
            // Such request isn't well optimized by postgres, and create different requests clogging up
            // pg_stat_statements output, making it impossible to analyze the performance impact of this query.
            // On top of this, the entity version is doing 2 left join to satisfy the Include queries, resulting in n*m row returned for each transaction.
            // n being the number of children, m the number of parents.
            if (ctx.Database.IsNpgsql() && !queryObject.UseInefficientPath)
            {
                var connection = ctx.Database.GetDbConnection();
                if (connection.State != System.Data.ConnectionState.Open)
                    await connection.OpenAsync();

                string walletIdFilter = queryObject.WalletId is not null ? " AND wos.\"WalletId\"=@walletId" : "";
                string typeFilter = queryObject.Type is not null ? " AND wos.\"Type\"=@type" : "";
                var cmd = connection.CreateCommand();
                var selectWalletObjects =
                    queryObject.TypesIds is not null ?
                    $"SELECT wos.* FROM unnest(@ids, @types) t(i,t) JOIN \"WalletObjects\" wos ON true{walletIdFilter} AND wos.\"Type\"=t AND wos.\"Id\"=i" :
                    queryObject.Ids is null ?
                    $"SELECT wos.* FROM \"WalletObjects\" wos WHERE true{walletIdFilter}{typeFilter} " :
                    queryObject.Ids.Length == 1 ?
                    $"SELECT wos.* FROM \"WalletObjects\" wos WHERE true{walletIdFilter} AND wos.\"Type\"=@type AND wos.\"Id\"=@id" :
                    $"SELECT wos.* FROM unnest(@ids) t JOIN \"WalletObjects\" wos ON true{walletIdFilter} AND wos.\"Type\"=@type AND wos.\"Id\"=t";

                var includeNeighbourSelect = queryObject.IncludeNeighbours ? ", wos2.\"Data\" AS \"Data2\"" : "";
                var includeNeighbourJoin = queryObject.IncludeNeighbours ? "LEFT JOIN \"WalletObjects\" wos2 ON wos.\"WalletId\"=wos2.\"WalletId\" AND wol.\"Type2\"=wos2.\"Type\" AND wol.\"Id2\"=wos2.\"Id\"" : "";
                var query =
                    $"SELECT wos.\"WalletId\", wos.\"Id\", wos.\"Type\", wos.\"Data\", wol.\"LinkData\", wol.\"Type2\", wol.\"Id2\"{includeNeighbourSelect} FROM ({selectWalletObjects}) wos " +
                    $"LEFT JOIN LATERAL ( " +
                    "SELECT \"AType\" AS \"Type2\", \"AId\" AS \"Id2\", \"Data\" AS \"LinkData\" FROM \"WalletObjectLinks\" WHERE \"WalletId\"=wos.\"WalletId\" AND \"BType\"=wos.\"Type\" AND \"BId\"=wos.\"Id\" " +
                    "UNION " +
                    "SELECT \"BType\" AS \"Type2\", \"BId\" AS \"Id2\", \"Data\" AS \"LinkData\" FROM \"WalletObjectLinks\" WHERE \"WalletId\"=wos.\"WalletId\" AND \"AType\"=wos.\"Type\" AND \"AId\"=wos.\"Id\"" +
                    $" ) wol ON true " + includeNeighbourJoin;
                cmd.CommandText = query;
                if (queryObject.WalletId is not null)
                {
                    var walletIdParam = cmd.CreateParameter();
                    walletIdParam.ParameterName = "walletId";
                    walletIdParam.Value = queryObject.WalletId.ToString();
                    walletIdParam.DbType = System.Data.DbType.String;
                    cmd.Parameters.Add(walletIdParam);
                }

                if (queryObject.Type != null)
                {
                    var typeParam = cmd.CreateParameter();
                    typeParam.ParameterName = "type";
                    typeParam.Value = queryObject.Type;
                    typeParam.DbType = System.Data.DbType.String;
                    cmd.Parameters.Add(typeParam);
                }

                if (queryObject.TypesIds != null)
                {
                    var typesParam = cmd.CreateParameter();
                    typesParam.ParameterName = "types";
                    typesParam.Value = queryObject.TypesIds.Select(t => t.Type).ToList();
                    typesParam.DbType = System.Data.DbType.Object;
                    cmd.Parameters.Add(typesParam);
                    var idParam = cmd.CreateParameter();
                    idParam.ParameterName = "ids";
                    idParam.Value = queryObject.TypesIds.Select(t => t.Id).ToList();
                    idParam.DbType = System.Data.DbType.Object;
                    cmd.Parameters.Add(idParam);
                }

                if (queryObject.Ids != null)
                {
                    if (queryObject.Ids.Length == 1)
                    {
                        var txIdParam = cmd.CreateParameter();
                        txIdParam.ParameterName = "id";
                        txIdParam.Value = queryObject.Ids[0];
                        txIdParam.DbType = System.Data.DbType.String;
                        cmd.Parameters.Add(txIdParam);
                    }
                    else
                    {
                        var txIdsParam = cmd.CreateParameter();
                        txIdsParam.ParameterName = "ids";
                        txIdsParam.Value = queryObject.Ids.ToList();
                        txIdsParam.DbType = System.Data.DbType.Object;
                        cmd.Parameters.Add(txIdsParam);
                    }
                }
                await using var reader = await cmd.ExecuteReaderAsync();
                var wosById = new Dictionary<WalletObjectId, WalletObjectData>();
                while (await reader.ReadAsync())
                {
                    WalletObjectData wo = new WalletObjectData();
                    wo.WalletId = (string)reader["WalletId"];
                    wo.Type = (string)reader["Type"];
                    wo.Id = (string)reader["Id"];
                    var id = new WalletObjectId(WalletId.Parse(wo.WalletId), wo.Type, wo.Id);
                    wo.Data = reader["Data"] is DBNull ? null : (string)reader["Data"];
                    if (wosById.TryGetValue(id, out var wo2))
                        wo = wo2;
                    else
                    {
                        wosById.Add(id, wo);
                        wo.Bs = new List<WalletObjectLinkData>();
                    }
                    if (reader["Type2"] is not DBNull)
                    {
                        var l = new WalletObjectLinkData()
                        {
                            BType = (string)reader["Type2"],
                            BId = (string)reader["Id2"],
                            Data = reader["LinkData"] is DBNull ? null : (string)reader["LinkData"]
                        };
                        wo.Bs.Add(l);
                        l.B = new WalletObjectData()
                        {
                            Type = l.BType,
                            Id = l.BId,
                            Data = (!queryObject.IncludeNeighbours || reader["Data2"] is DBNull) ? null : (string)reader["Data2"]
                        };
                    }
                }
                return wosById;
            }
            else // Unefficient path
            {
                IQueryable<WalletObjectData> q;
                if (queryObject.TypesIds is not null)
                {
                    // Note this is problematic if the type contains '##', but I don't see how to do it properly with entity framework
                    var idTypes = queryObject.TypesIds.Select(o => $"{o.Type}##{o.Id}").ToArray();
                    q = ctx.WalletObjects
                        .Where(w => (queryObject.WalletId == null || w.WalletId == queryObject.WalletId.ToString()) && idTypes.Contains(w.Type + "##" + w.Id));
                }
                else
                {
                    q = ctx.WalletObjects
                        .Where(w => (queryObject.WalletId == null || w.WalletId == queryObject.WalletId.ToString()) && (queryObject.Type == null || w.Type == queryObject.Type) && (queryObject.Ids == null || queryObject.Ids.Contains(w.Id)));
                }
                if (queryObject.IncludeNeighbours)
                {
                    q = q.Include(o => o.Bs).ThenInclude(o => o.B)
                        .Include(o => o.As).ThenInclude(o => o.A);
                }
                q = q.AsNoTracking();

                var wosById = new Dictionary<WalletObjectId, WalletObjectData>();
                foreach (var row in await q.ToListAsync())
                {
                    var id = new WalletObjectId(WalletId.Parse(row.WalletId), row.Type, row.Id);
                    wosById.TryAdd(id, row);
                }
                return wosById;
            }
        }
#nullable restore

        public async Task<Dictionary<string, WalletTransactionInfo>> GetWalletTransactionsInfo(WalletId walletId,
            string[] transactionIds = null)
        {
            var wos = await GetWalletObjects(
                new GetWalletObjectsQuery(walletId, WalletObjectData.Types.Tx, transactionIds));
            return await GetWalletTransactionsInfoCore(walletId, wos);
        }

        public async Task<Dictionary<string, WalletTransactionInfo>> GetWalletTransactionsInfo(WalletId walletId,
            ObjectTypeId[] transactionIds = null)
        {
            var wos = await GetWalletObjects(
                new GetWalletObjectsQuery(walletId, transactionIds));
            
            return await GetWalletTransactionsInfoCore(walletId, wos);
        }

        private async Task<Dictionary<string, WalletTransactionInfo>> GetWalletTransactionsInfoCore(WalletId walletId,
            Dictionary<WalletObjectId, WalletObjectData> wos)
        {
       
            var result = new Dictionary<string, WalletTransactionInfo>(wos.Count);
            foreach (var obj in wos.Values)
            {
                var data = obj.Data is null ? null : JObject.Parse(obj.Data);
                var info = new WalletTransactionInfo(walletId)
                {
                    Comment = data?["comment"]?.Value<string>()
                };
                result.Add(obj.Id, info);
                foreach (var neighbour in obj.GetNeighbours())
                {
                    var neighbourData = neighbour.Data is null ? null : JObject.Parse(neighbour.Data);
                    if (neighbour.Type == WalletObjectData.Types.Label)
                    {
                        info.LabelColors.TryAdd(neighbour.Id, neighbourData?["color"]?.Value<string>() ?? "#000");
                    }
                    else
                    {
                        info.Attachments.Add(new Attachment(neighbour.Type, neighbour.Id, neighbourData));
                    }
                }
            }
            return result;
        }


#nullable enable

        public async Task<(string Label, string Color)[]> GetWalletLabels(WalletId walletId)
        {
            await using var ctx = _ContextFactory.CreateContext();
            return (await
                    ctx.WalletObjects.AsNoTracking().Where(w => w.WalletId == walletId.ToString() && w.Type == WalletObjectData.Types.Label)
                    .ToArrayAsync())
                    .Select(o => (o.Id, JObject.Parse(o.Data)["color"]!.Value<string>()!)).ToArray();
        }

        public async Task<bool> RemoveWalletObjects(WalletObjectId walletObjectId)
        {
            await using var ctx = _ContextFactory.CreateContext();
            var entity = new WalletObjectData()
            {
                WalletId = walletObjectId.WalletId.ToString(),
                Type = walletObjectId.Type,
                Id = walletObjectId.Id
            };
            ctx.WalletObjects.Add(entity);
            ctx.Entry(entity).State = EntityState.Deleted;
            try
            {
                await ctx.SaveChangesAsync();
                return true;
            }
            catch (DbUpdateException) // doesn't exists
            {
                return false;
            }
        }

        public async Task EnsureWalletObjectLink(WalletObjectId a, WalletObjectId b, JObject? data = null, int infection = 0)
        {
            SortWalletObjectLinks(ref a, ref b);
            await using var ctx = _ContextFactory.CreateContext();
            var l = new WalletObjectLinkData()
            {
                WalletId = a.WalletId.ToString(),
                AType = a.Type,
                AId = a.Id,
                BType = b.Type,
                BId = b.Id,
                Data = data?.ToString(Formatting.None),
                InfectionRate = infection
            };
            ctx.WalletObjectLinks.Add(l);
            try
            {
                await ctx.SaveChangesAsync();
                await SpreadInfection(a, b, data, infection);
            }
            catch (DbUpdateException) // already exists
            {
            }
        }

        private async Task SpreadInfection(WalletObjectId a, WalletObjectId b, JObject? data = null, int infectionRate = 0 )
        {
            SortWalletObjectLinks(ref a, ref b);
            await using var ctx = _ContextFactory.CreateContext();
            var contagion = await ctx.WalletObjectLinks
                .Include(data => data.Parent)
                .ThenInclude(data => data.ChildLinks)
                .Include(data => data.Parent)
                .ThenInclude(data => data.ParentLinks)
                .Include(data => data.Child)
                .ThenInclude(data => data.ChildLinks)
                .Include(data => data.Child)
                .ThenInclude(data => data.ParentLinks)
                .SingleOrDefaultAsync(data =>
                data.WalletId == a.WalletId.ToString() &&
                data.ParentType == a.Type &&
                data.ParentId == a.Id &&
                data.ChildType == b.Type &&
                data.ChildId == b.Id
            );
            if (contagion is null)
            {
                await EnsureWalletObjectLink(a, b, data, infectionRate);
            }
            if ( contagion.InfectionRate == 0)
            {
                return;
            }

            foreach (WalletObjectData parentNeighbour in contagion.Parent.GetNeighbours())
            {
                await EnsureWalletObjectLink(b, new WalletObjectId(b.WalletId, parentNeighbour.Type, parentNeighbour.Id), data, infectionRate-1);
            }
            foreach (WalletObjectData childNeighbour in contagion.Child.GetNeighbours())
            {
                await EnsureWalletObjectLink(a, new WalletObjectId(a.WalletId, childNeighbour.Type, childNeighbour.Id), data, infectionRate-1);
            }
        }

        class WalletObjectIdComparer : IComparer<WalletObjectId>
        {
            public static readonly WalletObjectIdComparer Instance = new WalletObjectIdComparer();
            public int Compare(WalletObjectId? x, WalletObjectId? y)
            {
                var c = StringComparer.InvariantCulture.Compare(x?.Type, y?.Type);
                if (c == 0)
                    c = StringComparer.InvariantCulture.Compare(x?.Id, y?.Id);
                return c;
            }
        }

        private void SortWalletObjectLinks(ref WalletObjectId a, ref WalletObjectId b)
        {
            if (a.WalletId != b.WalletId)
                throw new ArgumentException("It shouldn't be possible to set a link between different wallets");
            var ab = new[] { a, b };
            Array.Sort(ab, WalletObjectIdComparer.Instance);
            a = ab[0];
            b = ab[1];
        }
        public async Task SetWalletObjectLink(WalletObjectId a, WalletObjectId b, JObject? data = null)
        {
            SortWalletObjectLinks(ref a, ref b);


            await using var ctx = _ContextFactory.CreateContext();
            var l = new WalletObjectLinkData()
            {
                WalletId = a.WalletId.ToString(),
                AType = a.Type,
                AId = a.Id,
                BType = b.Type,
                BId = b.Id,
                Data = data?.ToString(Formatting.None)
            };
            var e = ctx.WalletObjectLinks.Add(l);
            try
            {
                await ctx.SaveChangesAsync();
            }
            catch (DbUpdateException) // already exists
            {
                e.State = EntityState.Modified;
                await ctx.SaveChangesAsync();
            }
        }

        public static int MaxCommentSize = 200;
        public async Task SetWalletObjectComment(WalletObjectId id, string comment)
        {
            ArgumentNullException.ThrowIfNull(id);
            ArgumentNullException.ThrowIfNull(comment);
            if (!string.IsNullOrEmpty(comment))
                await ModifyWalletObjectData(id, (o) => o["comment"] = comment.Trim().Truncate(MaxCommentSize));
            else
                await ModifyWalletObjectData(id, (o) => o.Remove("comment"));
        }


        static WalletObjectData NewWalletObjectData(WalletObjectId id, JObject? data = null)
        {
            return new WalletObjectData()
            {
                WalletId = id.WalletId.ToString(),
                Type = id.Type,
                Id = id.Id,
                Data = data?.ToString()
            };
        }
        public async Task ModifyWalletObjectData(WalletObjectId id, Action<JObject> modify)
        {
            ArgumentNullException.ThrowIfNull(id);
            ArgumentNullException.ThrowIfNull(modify);
            using var ctx = _ContextFactory.CreateContext();
            var obj = await ctx.WalletObjects.FindAsync(id.WalletId.ToString(), id.Type, id.Id);
            if (obj is null)
            {
                obj = NewWalletObjectData(id);
                ctx.WalletObjects.Add(obj);
            }
            var currentData = obj.Data is null ? new JObject() : JObject.Parse(obj.Data);
            modify(currentData);
            obj.Data = currentData.ToString();
            if (obj.Data == "{}")
                obj.Data = null;
            await ctx.SaveChangesAsync();
        }

        const int MaxLabelSize = 50;
        public async Task AddWalletObjectLabels(WalletObjectId id, params string[] labels)
        {
            ArgumentNullException.ThrowIfNull(id);
            await EnsureWalletObject(id);
            foreach (var l in labels.Select(l => l.Trim().Truncate(MaxLabelSize)))
            {
                var labelObjId = new WalletObjectId(id.WalletId, WalletObjectData.Types.Label, l);
                await EnsureWalletObject(labelObjId, new JObject()
                {
                    ["color"] = ColorPalette.Default.DeterministicColor(l)
                });
                await EnsureWalletObjectLink(labelObjId, id);
            }
        }
        public Task AddWalletTransactionAttachment(WalletId walletId, uint256 txId, Attachment attachment)
        {
            return AddWalletTransactionAttachment(walletId, txId.ToString(), new []{attachment}, WalletObjectData.Types.Tx);
        }

        public Task AddWalletTransactionAttachment(WalletId walletId, uint256 txId,
            IEnumerable<Attachment> attachments)
        {
            return AddWalletTransactionAttachment(walletId, txId.ToString(), attachments, WalletObjectData.Types.Tx);
        }
        
        public async Task AddWalletTransactionAttachment(WalletId walletId, string txId, IEnumerable<Attachment> attachments, string type)
        {
            ArgumentNullException.ThrowIfNull(walletId);
            ArgumentNullException.ThrowIfNull(txId);
            var txObjId = new WalletObjectId(walletId, type, txId.ToString());
            await EnsureWalletObject(txObjId);
            foreach (var attachment in attachments)
            {
                var labelObjId = new WalletObjectId(walletId, WalletObjectData.Types.Label, attachment.Type);
                await EnsureWalletObject(labelObjId, new JObject()
                {
                    ["color"] = ColorPalette.Default.DeterministicColor(attachment.Type)
                });
                await EnsureWalletObjectLink(labelObjId, txObjId);
                if (attachment.Data is not null || attachment.Id.Length != 0)
                {
                    var data = new WalletObjectId(walletId, attachment.Type, attachment.Id);
                    await EnsureWalletObject(data, attachment.Data);
                    await EnsureWalletObjectLink(data, txObjId);
                }
            }
        }

        public async Task<bool> RemoveWalletObjectLink(WalletObjectId a, WalletObjectId b)
        {
            SortWalletObjectLinks(ref a, ref b);
            await using var ctx = _ContextFactory.CreateContext();
            ctx.WalletObjectLinks.Remove(new WalletObjectLinkData()
            {
                WalletId = a.WalletId.ToString(),
                AId = a.Id,
                AType = a.Type,
                BId = b.Id,
                BType = b.Type
            });
            try
            {
                await ctx.SaveChangesAsync();
                return true;
            }
            catch (DbUpdateException) // Already deleted, do nothing
            {
                return false;
            }
        }
        public async Task RemoveWalletObjectLabels(WalletObjectId id, params string[] labels)
        {
            ArgumentNullException.ThrowIfNull(id);
            foreach (var l in labels.Select(l => l.Trim()))
            {
                var labelObjId = new WalletObjectId(id.WalletId, WalletObjectData.Types.Label, l);
                await RemoveWalletObjectLink(labelObjId, id);
            }
        }

        public async Task SetWalletObject(WalletObjectId id, JObject? data)
        {
            ArgumentNullException.ThrowIfNull(id);
            await using var ctx = _ContextFactory.CreateContext();
            var o = NewWalletObjectData(id, data);
            ctx.WalletObjects.Add(o);
            try
            {
                await ctx.SaveChangesAsync();
            }
            catch (DbUpdateException) // already exists
            {
                ctx.Entry(o).State = EntityState.Modified;
                await ctx.SaveChangesAsync();
            }
        }

        public async Task EnsureWalletObject(WalletObjectId id, JObject? data = null)
        {
            ArgumentNullException.ThrowIfNull(id);
            await using var ctx = _ContextFactory.CreateContext();
            ctx.WalletObjects.Add(NewWalletObjectData(id, data));
            try
            {
                await ctx.SaveChangesAsync();
            }
            catch (DbUpdateException) // already exists
            {
            }
        }
#nullable restore
    }
}
