using System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace BTCPayServer.Logging
{
    public class Logs
    {
        private ILoggerFactory memoFactory;
        private bool isSerilogInit = false;

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
                if (!isSerilogInit)
                {
                    factory.AddSerilog(Serilog.Log.Logger);
                    isSerilogInit = true;
                }
                Configuration = factory.CreateLogger("Configuration");
                PayServer = factory.CreateLogger("PayServer");
                Events = factory.CreateLogger("Events");
                memoFactory = factory;
           }
        }

        public void AddSerilog()
        {
            memoFactory.AddSerilog(Serilog.Log.Logger);
            Configuration = memoFactory.CreateLogger("Configuration");
            PayServer = memoFactory.CreateLogger("PayServer");
            Events = memoFactory.CreateLogger("Events");
            isSerilogInit = true;
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
