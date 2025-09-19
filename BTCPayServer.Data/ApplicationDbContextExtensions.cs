#nullable enable
using System;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data.Subscriptions;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Data;

public static partial class ApplicationDbContextExtensions
{
    public static DbContext GetDbContext<T>(this DbSet<T> dbSet) where T : class
    {
        var infrastructure = dbSet as IInfrastructure<IServiceProvider>;
        var serviceProvider = infrastructure.Instance;
        var currentDbContext = (ICurrentDbContext)serviceProvider.GetService(typeof(ICurrentDbContext))!;
        return currentDbContext.Context;
    }

    public static DbConnection GetDbConnection<T>(this DbSet<T> dbSet) where T : class
        => dbSet.GetDbContext().Database.GetDbConnection();
}
