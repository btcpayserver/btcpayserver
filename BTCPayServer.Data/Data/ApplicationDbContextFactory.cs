using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Data
{
    public class ApplicationDbContextFactory : BaseDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContextFactory(DatabaseOptions options) : base(options, "")
        {
        }

        public override ApplicationDbContext CreateContext()
        {
            var builder = new DbContextOptionsBuilder<ApplicationDbContext>();
            ConfigureBuilder(builder);
            return new ApplicationDbContext(builder.Options);
        }
    }
}
