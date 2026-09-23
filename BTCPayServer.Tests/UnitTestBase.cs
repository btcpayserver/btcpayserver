using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Hosting;
using BTCPayServer.Logging;
using BTCPayServer.Plugins.Bitcoin;
using BTCPayServer.Plugins;
using BTCPayServer.Tests.Logging;
using Microsoft.Extensions.DependencyInjection;
using NBXplorer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;
using NBitcoin;
using Xunit;
using Xunit.v3;

namespace BTCPayServer.Tests
{
    [Collection(nameof(NonParallelizableCollectionDefinition))]
    public class UnitTestBase
    {
        public UnitTestBase(ITestOutputHelper helper)
        {
            TestLogs = new XUnitLog(helper) { Name = "Tests" };
            TestLogProvider = new XUnitLogProvider(helper);
            BTCPayLogs = new BTCPayServer.Logging.Logs();
            LoggerFactory = new BTCPayServer.Logging.FuncLoggerFactory((n) => new XUnitLog(helper) { Name = n });
            BTCPayLogs.Configure(LoggerFactory);
        }

        public DatabaseTester CreateDBTester()
        {
            return new DatabaseTester(TestLogs, LoggerFactory);
        }

        public BTCPayNetworkProvider CreateNetworkProvider(ChainName chainName)
        {
            var conf = new ConfigurationRoot(new List<IConfigurationProvider>()
            {
                new MemoryConfigurationProvider(new MemoryConfigurationSource()
                {
                    InitialData = new[] {
                        new KeyValuePair<string, string>("chains", "*"),
                        new KeyValuePair<string, string>("network", chainName.ToString())
                    }
                })
            });
            return CreateNetworkProvider(conf);
        }
        public BTCPayNetworkProvider CreateNetworkProvider(IConfiguration conf = null)
        {
            conf ??= new ConfigurationRoot(new List<IConfigurationProvider>()
            {
                new MemoryConfigurationProvider(new MemoryConfigurationSource()
                {
                    InitialData = new[] {
                        new KeyValuePair<string, string>("chains", "*"),
                        new KeyValuePair<string, string>("network", "regtest")
                    }
                })
            });
            var bootstrap = Startup.CreateBootstrap(conf);
            var services = new PluginServiceCollection(new ServiceCollection(), bootstrap);
            var plugins = new List<BaseBTCPayServerPlugin>() { new BitcoinPlugin() };

            plugins.Add(new BTCPayServer.Plugins.Altcoins.AltcoinsPlugin());

            foreach (var p in plugins)
            {
                p.Execute(services);
            }
            services.AddSingleton(services.BootstrapServices.GetRequiredService<SelectedChains>());
            services.AddSingleton(services.BootstrapServices.GetRequiredService<NBXplorerNetworkProvider>());
            services.AddSingleton(services.BootstrapServices.GetRequiredService<Logs>());
            services.AddSingleton(services.BootstrapServices.GetRequiredService<IConfiguration>());
            services.AddSingleton<BTCPayNetworkProvider>();
            var serviceProvider = services.BuildServiceProvider();
            return serviceProvider.GetService<BTCPayNetworkProvider>();
        }
        public ILog TestLogs
        {
            get;
        }
        public XUnitLogProvider TestLogProvider
        {
            get;
        }
        public BTCPayServer.Logging.Logs BTCPayLogs { get; }
        public FuncLoggerFactory LoggerFactory { get; }

        public ServerTester CreateServerTester([CallerMemberNameAttribute] string scope = null, bool newDb = false, TimeSpan? timeout = null)
        {
            return new ServerTester(scope, newDb, TestLogs, TestLogProvider, CreateNetworkProvider(),
                TestContext.Current.CancellationToken, GetTesterTimeout(timeout));
        }
        public PlaywrightTester CreatePlaywrightTester([CallerMemberNameAttribute] string scope = null, bool newDb = false, TimeSpan? timeout = null)
        {
            return new PlaywrightTester
            {
                Server = new ServerTester(scope, newDb, TestLogs, TestLogProvider, CreateNetworkProvider(),
                    TestContext.Current.CancellationToken, GetTesterTimeout(timeout))
            };
        }

        TimeSpan GetTesterTimeout(TimeSpan? timeout)
        {
            if (timeout is not null)
                return timeout.Value;

            var testTimeout = TestContext.Current.Test?.TestCase is IXunitTestCase testCase ? testCase.Timeout : 0;
            return TimeSpan.FromMilliseconds(testTimeout > 0 ? testTimeout : TestUtils.TestTimeout);
        }
    }
}
