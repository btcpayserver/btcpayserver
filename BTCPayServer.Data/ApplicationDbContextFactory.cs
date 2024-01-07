using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BTCPayServer.Data
{
    public class ApplicationDbContextFactory : BaseDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContextFactory(IOptions<DatabaseOptions> options) : base(options, "")
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
