using System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BTCPayServer.Logging
{
    public class Logs
    {
        public Logs()
        {
            Configure(new FuncLoggerFactory(n => NullLogger.Instance));
        }
        public void Configure(ILoggerFactory factory)
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
        public ILogger Configuration
        {
            get; set;
        }
        public ILogger PayServer
        {
            get; set;
        }

        public ILogger Events
        {
            get; set;
        }

        public const int ColumnLength = 16;
    }

    public class FuncLoggerFactory : ILoggerFactory
    {
        private readonly Func<string, ILogger> createLogger;
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
