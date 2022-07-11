using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Plugins.Test.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace BTCPayServer.Plugins.Test;

public class TestPluginMigrationRunner:IHostedService
{
    public class TestPluginDataMigrationHistory
    {
        public bool UpdatedSomething { get; set; }
    }
    private readonly TestPluginDbContextFactory _testPluginDbContextFactory;
    private readonly ISettingsRepository _settingsRepository;
    private readonly TestPluginService _testPluginService;

    public TestPluginMigrationRunner(TestPluginDbContextFactory testPluginDbContextFactory, ISettingsRepository settingsRepository, TestPluginService testPluginService)
    {
        _testPluginDbContextFactory = testPluginDbContextFactory;
        _settingsRepository = settingsRepository;
        _testPluginService = testPluginService;
    }
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var settings = await _settingsRepository.GetSettingAsync<TestPluginDataMigrationHistory>() ??
                       new TestPluginDataMigrationHistory();
        await using var ctx = _testPluginDbContextFactory.CreateContext();
        await ctx.Database.MigrateAsync(cancellationToken: cancellationToken);
        if (!settings.UpdatedSomething)
        {
            await _testPluginService.AddTestDataRecord();
            settings.UpdatedSomething = true;
            await _settingsRepository.UpdateSetting(settings);
        }
            
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
