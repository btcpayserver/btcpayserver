using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Logging;
using BTCPayServer.Tests.Logging;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace BTCPayServer.Tests.Fixtures
{
    public class FixtureBase
    {
        public XUnitLog TestLogs { get; }
        public XUnitLogProvider TestLogProvider { get; }
        public Logs BTCPayLogs { get; }

        class MessageSinkTestOutputHelper : ITestOutputHelper
        {
            private IMessageSink _messageSink;
            public MessageSinkTestOutputHelper(IMessageSink messageSink)
            {
                _messageSink = messageSink;
            }

            public ITestOutputHelper OutputHelper { get; set; }

            public void WriteLine(string message)
            {
                if (OutputHelper is null)
                    _messageSink.OnMessage(new DiagnosticMessage(message));
                else
                    OutputHelper.WriteLine(message);
            }

            public void WriteLine(string format, params object[] args)
            {
                if (OutputHelper is null)
                    _messageSink.OnMessage(new DiagnosticMessage(format, args));
                else
                    OutputHelper.WriteLine(format, args);
            }
        }


        public FixtureBase(IMessageSink sink)
        {
            var helper = new MessageSinkTestOutputHelper(sink);
            TestOutputHelper = helper;
            TestLogs = new XUnitLog(helper) { Name = "Setup" };
            TestLogProvider = new XUnitLogProvider(helper);
            BTCPayLogs = new BTCPayServer.Logging.Logs();
            BTCPayLogs.Configure(new BTCPayServer.Logging.FuncLoggerFactory((n) => new XUnitLog(helper) { Name = n }));
        }
        MessageSinkTestOutputHelper TestOutputHelper { get; }
        public ITestOutputHelper OutputHelper
        {
            get
            {
                return TestOutputHelper.OutputHelper;
            }
            set
            {
                TestOutputHelper.OutputHelper = value;
            }
        }

        public ServerTester CreateServerTester()
        {
            return new ServerTester(this.GetType().Name, true, TestLogs, TestLogProvider);
        }
        public SeleniumTester CreateSeleniumTester()
        {
            return new SeleniumTester() { Server = new ServerTester(this.GetType().Name, true, TestLogs, TestLogProvider) };
        }
    }
}
