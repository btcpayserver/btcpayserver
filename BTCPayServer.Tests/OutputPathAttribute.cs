using System;

namespace BTCPayServer.Tests
{
    public class OutputPathAttribute : Attribute
    {
        public OutputPathAttribute(string builtPath)
        {
            BuiltPath = builtPath;
        }
        public string BuiltPath { get; }
    }
}
