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
        }
        public ILog TestLogs
        {
            get;
        }
        public XUnitLogProvider TestLogProvider
        {
            get;
        }

        public ServerTester CreateServerTester([CallerMemberNameAttribute] string scope = null, bool newDb = false)
        {
            return new ServerTester(scope, newDb);
        }
        public SeleniumTester CreateSeleniumTester([CallerMemberNameAttribute] string scope = null, bool newDb = false)
        {
            return new SeleniumTester() { Server = new ServerTester(scope, newDb) };
        }
    }
}
