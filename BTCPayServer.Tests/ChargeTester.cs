﻿using System;
using System.Collections.Generic;
using System.Text;
using BTCPayServer.Payments.Lightning.CLightning;
using NBitcoin;

namespace BTCPayServer.Tests
{
    public class ChargeTester
    {
        private ServerTester _Parent;

        public ChargeTester(ServerTester serverTester, string environmentName, string defaultValue, string defaultHost, Network network)
        {
            this._Parent = serverTester;
            var url = serverTester.GetEnvironment(environmentName, defaultValue);
            Client = new ChargeClient(new Uri(url), network);
            P2PHost = _Parent.GetEnvironment(environmentName + "_HOST", defaultHost);
        }        
        public ChargeClient Client { get; set; }
        public string P2PHost { get; }
    }
}
