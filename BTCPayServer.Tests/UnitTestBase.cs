using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using BTCPayServer.Tests.Logging;
using Xunit.Abstractions;

namespace BTCPayServer.Tests
{
    public class UnitTestBase
    {
        public UnitTestBase(ITestOutputHelper helper)
        {
            TestLogs = new XUnitLog(helper) { Name = "Tests" };
            TestLogProvider = new XUnitLogProvider(helper);
            BTCPayLogs = new BTCPayServer.Logging.Logs();
            BTCPayLogs.Configure(new BTCPayServer.Logging.FuncLoggerFactory((n) => new XUnitLog(helper) { Name = n }));
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

        public ServerTester CreateServerTester([CallerMemberNameAttribute] string scope = null, bool newDb = false)
        {
            return new ServerTester(scope, newDb, TestLogs, TestLogProvider);
        }
        public SeleniumTester CreateSeleniumTester([CallerMemberNameAttribute] string scope = null, bool newDb = false)
        {
            return new SeleniumTester() { Server = new ServerTester(scope, newDb, TestLogs, TestLogProvider) };
        }
    }
}
