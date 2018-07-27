using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Logging
{
    public class Logs
    {
        static Logs()
        {
            Configure(new FuncLoggerFactory(n => NullLogger.Instance));
        }
        public static void Configure(ILoggerFactory factory)
        {
            if (factory == null)
                Configure(new FuncLoggerFactory(n => NullLogger.Instance));
            else
            {
                Configuration = factory.CreateLogger("Configuration");
                PayServer = factory.CreateLogger("PayServer");
                Events = factory.CreateLogger("Events");
            }
        }
        public static ILogger Configuration
        {
            get; set;
        }
        public static ILogger PayServer
        {
            get; set;
        }

        public static ILogger Events
        {
            get; set;
        }

        public const int ColumnLength = 16;
    }

    public class FuncLoggerFactory : ILoggerFactory
    {
        private Func<string, ILogger> createLogger;
        public FuncLoggerFactory(Func<string, ILogger> createLogger)
        {
            this.createLogger = createLogger;
        }
        public void AddProvider(ILoggerProvider provider)
        {

        }

        public ILogger CreateLogger(string categoryName)
        {
            return createLogger(categoryName);
        }

        public void Dispose()
        {

        }
    }
}
