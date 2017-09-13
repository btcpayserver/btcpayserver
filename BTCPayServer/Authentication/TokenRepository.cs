using DBreeze;
using NBitcoin;
using NBitcoin.DataEncoders;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace BTCPayServer.Authentication
{
	public class TokenRepository
	{
		public TokenRepository(DBreezeEngine engine)
		{
			_Engine = engine;
		}


		private readonly DBreezeEngine _Engine;
		public DBreezeEngine Engine
		{
			get
			{
				return _Engine;
			}
		}

		public Task<BitTokenEntity[]> GetTokens(string sin)
		{
			List<BitTokenEntity> tokens = new List<BitTokenEntity>();
			using(var tx = _Engine.GetTransaction())
			{
				tx.ValuesLazyLoadingIsOn = false;
				foreach(var row in tx.SelectForward<string, byte[]>($"T_{sin}"))
				{
					var token = ToObject<BitTokenEntity>(row.Value);
					tokens.Add(token);
				}
			}
			return Task.FromResult(tokens.ToArray());
		}

		public Task<BitTokenEntity> CreateToken(string sin, string tokenName)
		{
			var token = new BitTokenEntity
			{
				Name = tokenName,
				Value = Encoders.Base58.EncodeData(RandomUtils.GetBytes(32)),
				DateCreated = DateTimeOffset.UtcNow
			};
			using(var tx = _Engine.GetTransaction())
			{
				tx.Insert<string, byte[]>($"T_{sin}", token.Name, ToBytes(token));
				tx.Commit();
			}
			return Task.FromResult(token);
		}

		public Task<bool> PairWithAsync(string pairingCode, string pairedId)
		{
			if(pairedId == null)
				throw new ArgumentNullException(nameof(pairedId));
			using(var tx = _Engine.GetTransaction())
			{
				var row = tx.Select<string, byte[]>("PairingCodes", pairingCode);
				if(row == null || !row.Exists)
					return Task.FromResult(false);
				tx.RemoveKey<string>("PairingCodes", pairingCode);
				try
				{
					var pairingEntity = ToObject<PairingCodeEntity>(row.Value);
					if(pairingEntity.IsExpired())
						return Task.FromResult(false);
					row = tx.Select<string, byte[]>($"T_{pairingEntity.SIN}", pairingEntity.Facade);
					if(row == null || !row.Exists)
						return Task.FromResult(false);
					var token = ToObject<BitTokenEntity>(row.Value);
					if(token.Active)
						return Task.FromResult(false);
					token.Active = true;
					token.PairedId = pairedId;
					token.SIN = pairingEntity.SIN;
					token.Label = pairingEntity.Label;
					token.PairingTime = DateTimeOffset.UtcNow;
					tx.Insert($"TbP_{pairedId}", token.Value, ToBytes(token));
					tx.Insert($"T_{pairingEntity.SIN}", pairingEntity.Facade, ToBytes(token));
				}
				finally
				{
					tx.Commit();
				}
			}
			return Task.FromResult(true);
		}

		public Task<BitTokenEntity[]> GetTokensByPairedIdAsync(string pairedId)
		{
			List<BitTokenEntity> tokens = new List<BitTokenEntity>();
			using(var tx = _Engine.GetTransaction())
			{
				tx.ValuesLazyLoadingIsOn = false;
				foreach(var row in tx.SelectForward<string, byte[]>($"TbP_{pairedId}"))
				{
					tokens.Add(ToObject<BitTokenEntity>(row.Value));
				}
			}
			return Task.FromResult(tokens.ToArray());
		}

		public Task<PairingCodeEntity> GetPairingAsync(string pairingCode)
		{
			using(var tx = _Engine.GetTransaction())
			{
				var row = tx.Select<string, byte[]>("PairingCodes", pairingCode);
				if(row == null || !row.Exists)
					return Task.FromResult<PairingCodeEntity>(null);
				var pairingEntity = ToObject<PairingCodeEntity>(row.Value);
				if(pairingEntity.IsExpired())
					return Task.FromResult<PairingCodeEntity>(null);
				return Task.FromResult(pairingEntity);
			}
		}
		public Task<PairingCodeEntity> AddPairingCodeAsync(PairingCodeEntity pairingCodeEntity)
		{
			pairingCodeEntity = Clone(pairingCodeEntity);
			pairingCodeEntity.Id = Encoders.Base58.EncodeData(RandomUtils.GetBytes(6));
			using(var tx = _Engine.GetTransaction())
			{
				tx.Insert("PairingCodes", pairingCodeEntity.Id, ToBytes(pairingCodeEntity));
				tx.Commit();
			}
			return Task.FromResult(pairingCodeEntity);
		}

		private byte[] ToBytes<T>(T obj)
		{
			return ZipUtils.Zip(JsonConvert.SerializeObject(obj));
		}
		private T ToObject<T>(byte[] value)
		{
			return JsonConvert.DeserializeObject<T>(ZipUtils.Unzip(value));
		}

		private T Clone<T>(T obj)
		{
			return ToObject<T>(ToBytes(obj));
		}


		public async Task<bool> DeleteToken(string sin, string tokenName, string storeId)
		{
			var token = await GetToken(sin, tokenName);
			if(token == null || (token.PairedId != null && token.PairedId != storeId))
				return false;
			using(var tx = _Engine.GetTransaction())
			{
				tx.RemoveKey<string>($"T_{sin}", tokenName);
				if(token.PairedId != null)
					tx.RemoveKey<string>($"TbP_" + token.PairedId, token.Value);
				tx.Commit();
			}
			return true;
		}

		public Task<BitTokenEntity> GetToken(string sin, string tokenName)
		{
			using(var tx = _Engine.GetTransaction())
			{
				tx.ValuesLazyLoadingIsOn = true;
				var row = tx.Select<string, byte[]>($"T_{sin}", tokenName);
				if(row == null || !row.Exists)
					return Task.FromResult<BitTokenEntity>(null);
				var token = ToObject<BitTokenEntity>(row.Value);
				if(!token.Active)
					return Task.FromResult<BitTokenEntity>(null);
				return Task.FromResult(token);
			}
		}

	}
}
