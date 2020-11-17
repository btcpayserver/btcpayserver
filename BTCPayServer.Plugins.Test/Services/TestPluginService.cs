using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BTCPayServer.Plugins.Test.Data;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Plugins.Test.Services
{
    public class TestPluginService
    {
        private readonly TestPluginDbContextFactory _testPluginDbContextFactory;

        public TestPluginService(TestPluginDbContextFactory testPluginDbContextFactory)
        {
            _testPluginDbContextFactory = testPluginDbContextFactory;
        }

        public async Task AddTestDataRecord()
        {
            await using var context = _testPluginDbContextFactory.CreateContext();

            await context.TestPluginRecords.AddAsync(new TestPluginData() {Timestamp = DateTimeOffset.UtcNow});
        }
        
        
        public async Task<List<TestPluginData>> Get()
        {
            await using var context = _testPluginDbContextFactory.CreateContext();

            return await context.TestPluginRecords.ToListAsync();
        }
    }
}
