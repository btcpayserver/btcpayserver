using System.Reflection;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Plugins.Test.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BTCPayServer.Plugins.Test
{
    
    public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<TestPluginDbContext>
    {
        public TestPluginDbContext CreateDbContext(string[] args)
        {

            var builder = new DbContextOptionsBuilder<TestPluginDbContext>();

            builder.UseSqlite("Data Source=temp.db");

            return new TestPluginDbContext(builder.Options, true);
        }
    }

    public class TestPluginDbContextFactory : BaseDbContextFactory<TestPluginDbContext>
    {
        public TestPluginDbContextFactory(DatabaseOptions options) : base(options, "BTCPayServer.Plugins.Test")
        {
        }

        public override TestPluginDbContext CreateContext()
        {
            var builder = new DbContextOptionsBuilder<TestPluginDbContext>();
            ConfigureBuilder(builder);
            return new TestPluginDbContext(builder.Options);
            
        }
    }
}
