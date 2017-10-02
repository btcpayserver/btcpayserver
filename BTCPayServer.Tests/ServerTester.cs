using BTCPayServer.Controllers;
using BTCPayServer.Models.AccountViewModels;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;
using NBitcoin.RPC;
using NBitpayClient;
using NBXplorer;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace BTCPayServer.Tests
{
	public class ServerTester : IDisposable
	{
		public static ServerTester Create([CallerMemberNameAttribute]string scope = null)
		{
			return new ServerTester(scope);
		}

		string _Directory;
		public ServerTester(string scope)
		{
			_Directory = scope;
		}

		public void Start()
		{
			if(Directory.Exists(_Directory))
				Utils.DeleteDirectory(_Directory);
			if(!Directory.Exists(_Directory))
				Directory.CreateDirectory(_Directory);

			ExplorerNode = new RPCClient(RPCCredentialString.Parse(GetEnvironment("TESTS_RPCCONNECTION", "server=http://127.0.0.1:43782;ceiwHEbqWI83:DwubwWsoo3")), Network);
			ExplorerClient = new ExplorerClient(Network, new Uri(GetEnvironment("TESTS_NBXPLORERURL", "http://127.0.0.1:32838/")));
			PayTester = new BTCPayServerTester(Path.Combine(_Directory, "pay"))
			{
				NBXplorerUri = ExplorerClient.Address,
				Postgres = GetEnvironment("TESTS_POSTGRES", "User ID=postgres;Host=127.0.0.1;Port=39372;Database=btcpayserver")
			};
			PayTester.Start();
		}

		private string GetEnvironment(string variable, string defaultValue)
		{
			var var = Environment.GetEnvironmentVariable(variable);
			return String.IsNullOrEmpty(var) ? defaultValue : var;
		}

		public TestAccount CreateAccount()
		{
			return new TestAccount(this);
		}

		public RPCClient ExplorerNode
		{
			get; set;
		}

		public ExplorerClient ExplorerClient
		{
			get; set;
		}

		

		public BTCPayServerTester PayTester
		{
			get; set;
		}
		
		public Network Network
		{
			get;
			set;
		} = Network.RegTest;

		public void Dispose()
		{
			if(PayTester != null)
				PayTester.Dispose();
		}
	}
}
