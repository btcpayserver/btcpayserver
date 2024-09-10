using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Data
{
    /// <summary>
    /// We have a migration running in the background that will migrate the data from the old blob to the new blob
    /// Meanwhile, we need to make sure that invoices which haven't been migrated yet are migrated on the fly.
    /// </summary>
    public class MigrationInterceptor : IMaterializationInterceptor, ISaveChangesInterceptor
    {
        public interface IHasMigration
        {
            bool TryMigrate();
            bool Migrated { get; set; }
        }
        public static readonly MigrationInterceptor Instance = new MigrationInterceptor();
        public object InitializedInstance(MaterializationInterceptionData materializationData, object entity)
        {
            if (entity is IHasMigration hasMigration && hasMigration.TryMigrate())
            {
                hasMigration.Migrated = true;
            }
            return entity;
        }

        public ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default(CancellationToken))
        {

            foreach (var entry in eventData.Context.ChangeTracker.Entries())
            {
                if (entry is { Entity: IHasMigration { Migrated: true }, State: EntityState.Modified })
                    // It seems doing nothing, but this actually set all properties as modified
                    entry.State = EntityState.Modified;
            }
            return new ValueTask<InterceptionResult<int>>(result);
        }
    }
}
