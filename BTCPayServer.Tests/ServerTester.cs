using BTCPayServer.Controllers;
using BTCPayServer.Models.AccountViewModels;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;
using NBitcoin.Tests;
using NBitpayClient;
using System;
using System.Collections.Generic;
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
		NodeBuilder _Builder;

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
			_Builder = NodeBuilder.Create(_Directory, "0.14.2");
			ExplorerNode = _Builder.CreateNode(false);
			ExplorerNode.WhiteBind = true;
			ExplorerNode.Start();
			ExplorerNode.CreateRPCClient().Generate(101);
			ExplorerTester = NBXplorerTester.Create(Path.Combine(_Directory, "explorer"));
			ExplorerTester.Node = ExplorerNode;
			ExplorerTester.Start();

			PayTester = new BTCPayServerTester(Path.Combine(_Directory, "pay"))
			{
				NBXplorerUri = ExplorerTester.ExplorerClient.Address,
				CookieFile = ExplorerTester.CookieFile
			};
			PayTester.Start();
		}

		public TestAccount CreateAccount()
		{
			return new TestAccount(this);
		}

		public CoreNode ExplorerNode
		{
			get; set;
		}

		public BTCPayServerTester PayTester
		{
			get; set;
		}

		public NBXplorerTester ExplorerTester
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
			if(ExplorerTester != null)
				ExplorerTester.Dispose();
			if(_Builder != null)
				_Builder.Dispose();
		}
	}
}
