﻿using WalletWasabi.JsonConverters;
using WalletWasabi.Helpers;
using NBitcoin;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Security;
using System;
using WalletWasabi.Models;
using WalletWasabi.Hwi.Models;
using WalletWasabi.Logging;

namespace WalletWasabi.KeyManagement
{
	[JsonObject(MemberSerialization.OptIn)]
	public class KeyManager
	{
		[JsonProperty(Order = 1)]
		[JsonConverter(typeof(BitcoinEncryptedSecretNoECJsonConverter))]
		public BitcoinEncryptedSecretNoEC EncryptedSecret { get; }

		[JsonProperty(Order = 2)]
		[JsonConverter(typeof(ByteArrayJsonConverter))]
		public byte[] ChainCode { get; }

		[JsonProperty(Order = 3)]
		[JsonConverter(typeof(ExtPubKeyJsonConverter))]
		public ExtPubKey ExtPubKey { get; }

		[JsonProperty(Order = 4)]
		public bool? PasswordVerified { get; private set; }

		[JsonProperty(Order = 5)]
		private BlockchainState BlockchainState { get; }

		private object BlockchainStateLock { get; }

		[JsonProperty(Order = 6)]
		private List<HdPubKey> HdPubKeys { get; }

		private object HdPubKeysLock { get; }

		private List<byte[]> HdPubKeyScriptBytes { get; }

		private object HdPubKeyScriptBytesLock { get; }

		private Dictionary<Script, HdPubKey> ScriptHdPubkeyMap { get; }

		private object ScriptHdPubkeyMapLock { get; }

		// BIP84-ish derivation scheme
		// m / purpose' / coin_type' / account' / change / address_index
		// https://github.com/bitcoin/bips/blob/master/bip-0084.mediawiki
		public static readonly KeyPath AccountKeyPath = new KeyPath("m/84'/0'/0'");

		public string FilePath { get; private set; }
		private object ToFileLock { get; }

		public bool IsWatchOnly { get; }
		public bool IsHardwareWallet => HardwareWalletInfo != null;
		public HardwareWalletInfo HardwareWalletInfo { get; set; }

		[JsonConstructor]
		public KeyManager(BitcoinEncryptedSecretNoEC encryptedSecret, byte[] chainCode, ExtPubKey extPubKey, bool? passwordVerified, BlockchainState blockchainState, string filePath = null)
		{
			HdPubKeys = new List<HdPubKey>();
			HdPubKeyScriptBytes = new List<byte[]>();
			ScriptHdPubkeyMap = new Dictionary<Script, HdPubKey>();
			HdPubKeysLock = new object();
			HdPubKeyScriptBytesLock = new object();
			ScriptHdPubkeyMapLock = new object();
			BlockchainStateLock = new object();

			EncryptedSecret = encryptedSecret;
			IsWatchOnly = EncryptedSecret is null;
			ChainCode = chainCode;
			ExtPubKey = Guard.NotNull(nameof(extPubKey), extPubKey);

			PasswordVerified = passwordVerified;

			BlockchainState = blockchainState ?? new BlockchainState();
			HardwareWalletInfo = null;

			SetFilePath(filePath);
			ToFileLock = new object();
			ToFile();
		}

		public KeyManager(BitcoinEncryptedSecretNoEC encryptedSecret, byte[] chainCode, string password, string filePath = null)
		{
			HdPubKeys = new List<HdPubKey>();
			HdPubKeyScriptBytes = new List<byte[]>();
			ScriptHdPubkeyMap = new Dictionary<Script, HdPubKey>();
			HdPubKeysLock = new object();
			HdPubKeyScriptBytesLock = new object();
			ScriptHdPubkeyMapLock = new object();
			BlockchainState = new BlockchainState();
			BlockchainStateLock = new object();
			HardwareWalletInfo = null;

			if (password is null)
			{
				password = "";
			}

			EncryptedSecret = Guard.NotNull(nameof(encryptedSecret), encryptedSecret);
			IsWatchOnly = false;
			ChainCode = Guard.NotNull(nameof(chainCode), chainCode);
			var extKey = new ExtKey(encryptedSecret.GetKey(password), chainCode);

			ExtPubKey = extKey.Derive(AccountKeyPath).Neuter();

			SetFilePath(filePath);
			ToFileLock = new object();
			ToFile();
		}

		public static KeyManager CreateNew(out Mnemonic mnemonic, string password, string filePath = null)
		{
			if (password is null)
			{
				password = "";
			}

			mnemonic = new Mnemonic(Wordlist.English, WordCount.Twelve);
			ExtKey extKey = mnemonic.DeriveExtKey(password);
			var encryptedSecret = extKey.PrivateKey.GetEncryptedBitcoinSecret(password, Network.Main);

			return new KeyManager(encryptedSecret, extKey.ChainCode, extKey.Derive(AccountKeyPath).Neuter(), false, new BlockchainState(), filePath);
		}

		public static KeyManager CreateNewWatchOnly(ExtPubKey extPubKey, string filePath = null)
		{
			return new KeyManager(null, null, extPubKey, null, new BlockchainState(), filePath);
		}

		public static KeyManager Recover(Mnemonic mnemonic, string password, string filePath = null)
		{
			Guard.NotNull(nameof(mnemonic), mnemonic);
			if (password is null)
			{
				password = "";
			}

			ExtKey extKey = mnemonic.DeriveExtKey(password);
			var encryptedSecret = extKey.PrivateKey.GetEncryptedBitcoinSecret(password, Network.Main);

			return new KeyManager(encryptedSecret, extKey.ChainCode, extKey.Derive(AccountKeyPath).Neuter(), true, new BlockchainState(), filePath);
		}

		public void SetFilePath(string filePath)
		{
			FilePath = string.IsNullOrWhiteSpace(filePath) ? null : filePath;
			if (FilePath is null) return;
			IoHelpers.EnsureContainingDirectoryExists(FilePath);
		}

		public void ToFile()
		{
			lock (HdPubKeysLock)
				lock (BlockchainStateLock)
					lock (ToFileLock)
					{
						ToFileNoLock();
					}
		}

		public void ToFileNoBlockchainStateLock()
		{
			lock (HdPubKeysLock)
				lock (ToFileLock)
				{
					ToFileNoLock();
				}
		}

		public void ToFile(string filePath)
		{
			lock (HdPubKeysLock)
				lock (BlockchainStateLock)
					lock (ToFileLock)
					{
						ToFileNoLock(filePath);
					}
		}

		private void ToFileNoLock()
		{
			if (FilePath is null) return;
			ToFileNoLock(FilePath);
		}

		private void ToFileNoLock(string filePath)
		{
			IoHelpers.EnsureContainingDirectoryExists(filePath);
			// Remove the last 100 blocks to ensure verification on the next run. This is needed of reorg.
			int maturity = 101;
			Height prevHeight = BlockchainState.BestHeight;
			int matureHeight = Math.Max(0, prevHeight.Value - maturity);

			BlockchainState.BestHeight = new Height(matureHeight);
			HashSet<BlockState> toRemove = BlockchainState.BlockStates.Where(x => x.BlockHeight >= BlockchainState.BestHeight).ToHashSet();
			BlockchainState.BlockStates.RemoveAll(x => toRemove.Contains(x));

			string jsonString = JsonConvert.SerializeObject(this, Formatting.Indented);
			File.WriteAllText(filePath, jsonString, Encoding.UTF8);

			// Re-add removed items for further operations.
			BlockchainState.BlockStates.AddRange(toRemove.OrderBy(x => x));
			BlockchainState.BestHeight = prevHeight;
		}

		public static KeyManager FromFile(string filePath)
		{
			filePath = Guard.NotNullOrEmptyOrWhitespace(nameof(filePath), filePath);

			if (!IoHelpers.TryGetSafestFileVersion(filePath, out var safestFile))
			{
				throw new FileNotFoundException($"Wallet file not found at: `{filePath}`.");
			}

			string jsonString = File.ReadAllText(safestFile, Encoding.UTF8);
			var km = JsonConvert.DeserializeObject<KeyManager>(jsonString);
			km.SetFilePath(filePath);
			lock (km.HdPubKeyScriptBytesLock)
			{
				km.HdPubKeyScriptBytes.AddRange(km.GetKeys(x => true).Select(x => x.P2wpkhScript.ToCompressedBytes()));
			}

			lock (km.ScriptHdPubkeyMapLock)
			{
				foreach (var key in km.GetKeys())
				{
					km.ScriptHdPubkeyMap.Add(key.P2wpkhScript, key);
				}
			}
			return km;
		}

		public static ExtPubKey ExtPubKeyFromFile(string filePath)
		{
			filePath = Guard.NotNullOrEmptyOrWhitespace(nameof(filePath), filePath);

			if (!IoHelpers.TryGetSafestFileVersion(filePath, out var safestFile))
			{
				throw new FileNotFoundException($"Wallet file not found at: `{filePath}`.");
			}

			// Example text to handle: "ExtPubKey": "03BF8271268000000013B9013C881FE456DDF524764F6322F611B03CF6".

			var extpubkeyline = File.ReadLines(filePath) // Enumerated read.
				.Take(10) // Limit reads to x lines.
				.FirstOrDefault(line => line.Contains("\"ExtPubKey\": \"", StringComparison.InvariantCulture));

			if (string.IsNullOrEmpty(extpubkeyline))
				throw new InvalidOperationException($"Could not find line ExtPubKey in file: {filePath}");

			var parts = extpubkeyline.Split("\"ExtPubKey\": \"");
			if (parts.Length != 2)
				throw new FormatException($"Could not split line: {extpubkeyline}");

			var xpub = parts[1].TrimEnd(',', '"');

			ExtPubKey epk;

			try
			{
				epk = ExtPubKey.Parse(xpub);  // Starts with "ExtPubKey": "xpub...
			}
			catch
			{
				// Try hex, Old wallet format was like this.
				epk = new ExtPubKey(ByteHelpers.FromHex(xpub)); // Starts with "ExtPubKey": "hexbytes...
			}
			return epk;
		}

		public HdPubKey GenerateNewKey(string label, KeyState keyState, bool isInternal, bool toFile = true)
		{
			// BIP44-ish derivation scheme
			// m / purpose' / coin_type' / account' / change / address_index
			var change = isInternal ? 1 : 0;

			lock (HdPubKeysLock)
			{
				IEnumerable<HdPubKey> relevantHdPubKeys;
				if (isInternal)
				{
					relevantHdPubKeys = HdPubKeys.Where(x => x.IsInternal);
				}
				else
				{
					relevantHdPubKeys = HdPubKeys.Where(x => !x.IsInternal);
				}

				KeyPath path;
				if (!relevantHdPubKeys.Any())
				{
					path = new KeyPath($"{change}/0");
				}
				else
				{
					int largestIndex = relevantHdPubKeys.Max(x => x.Index);
					List<int> missingIndexes = Enumerable.Range(0, largestIndex).Except(relevantHdPubKeys.Select(x => x.Index)).ToList();
					if (missingIndexes.Any())
					{
						int smallestMissingIndex = missingIndexes.Min();
						path = relevantHdPubKeys.First(x => x.Index == (smallestMissingIndex - 1)).NonHardenedKeyPath.Increment();
					}
					else
					{
						path = relevantHdPubKeys.First(x => x.Index == largestIndex).NonHardenedKeyPath.Increment();
					}
				}

				var fullPath = AccountKeyPath.Derive(path);
				var pubKey = ExtPubKey.Derive(path).PubKey;

				var hdPubKey = new HdPubKey(pubKey, fullPath, label, keyState);
				HdPubKeys.Add(hdPubKey);
				lock (HdPubKeyScriptBytesLock)
				{
					HdPubKeyScriptBytes.Add(hdPubKey.P2wpkhScript.ToCompressedBytes());
				}

				lock (ScriptHdPubkeyMapLock)
				{
					ScriptHdPubkeyMap.Add(hdPubKey.P2wpkhScript, hdPubKey);
				}

				if (toFile)
				{
					ToFile();
				}

				return hdPubKey;
			}
		}

		public IEnumerable<HdPubKey> GetKeys(Func<HdPubKey, bool> wherePredicate)
		{
			// BIP44-ish derivation scheme
			// m / purpose' / coin_type' / account' / change / address_index
			lock (HdPubKeysLock)
			{
				if (wherePredicate is null)
				{
					return HdPubKeys.ToList();
				}
				else
				{
					return HdPubKeys.Where(wherePredicate).ToList();
				}
			}
		}

		public void SetPasswordVerified()
		{
			PasswordVerified = true;
			ToFile();
		}

		public IEnumerable<HdPubKey> GetKeys(KeyState? keyState = null, bool? isInternal = null)
		{
			if (keyState is null && isInternal is null)
			{
				return GetKeys(x => true);
			}
			if (isInternal is null && keyState != null)
			{
				return GetKeys(x => x.KeyState == keyState);
			}
			else if (keyState is null)
			{
				return GetKeys(x => x.IsInternal == isInternal);
			}
			return GetKeys(x => x.IsInternal == isInternal && x.KeyState == keyState);
		}

		public IEnumerable<byte[]> GetPubKeyScriptBytes()
		{
			lock (HdPubKeyScriptBytesLock)
			{
				return HdPubKeyScriptBytes;
			}
		}

		public HdPubKey GetKeyForScriptPubKey(Script scriptPubkey)
		{
			lock (ScriptHdPubkeyMapLock)
			{
				if (ScriptHdPubkeyMap.TryGetValue(scriptPubkey, out var key))
					return key;
				return default;
			}
		}

		public IEnumerable<ExtKey> GetSecrets(string password, params Script[] scripts)
		{
			return GetSecretsAndPubKeyPairs(password, scripts).Select(x => x.secret);
		}

		public IEnumerable<(ExtKey secret, HdPubKey pubKey)> GetSecretsAndPubKeyPairs(string password, params Script[] scripts)
		{
			ExtKey extKey = GetMasterExtKey(password);
			var extKeysAndPubs = new List<(ExtKey secret, HdPubKey pubKey)>();

			lock (HdPubKeysLock)
			{
				foreach (HdPubKey key in HdPubKeys.Where(x =>
					scripts.Contains(x.P2wpkhScript)
					|| scripts.Contains(x.P2shOverP2wpkhScript)
					|| scripts.Contains(x.P2pkhScript)
					|| scripts.Contains(x.P2pkScript)))
				{
					ExtKey ek = extKey.Derive(key.FullKeyPath);
					extKeysAndPubs.Add((ek, key));
				}
			}
			return extKeysAndPubs;
		}

		public ExtKey GetMasterExtKey(string password)
		{
			if (IsWatchOnly)
			{
				throw new SecurityException("This is a watchonly wallet.");
			}

			try
			{
				Key secret = EncryptedSecret.GetKey(password);
				return new ExtKey(secret, ChainCode);
			}
			catch (SecurityException ex)
			{
				throw new SecurityException("Invalid password.", ex);
			}
		}

		public bool TestPassword(string password)
		{
			try
			{
				GetMasterExtKey(password);
				return true;
			}
			catch
			{
				return false;
			}
		}

		/// <summary>
		/// Make sure there's always clean keys generated and indexed.
		/// </summary>
		public bool AssertCleanKeysIndexed(int howMany = 21, bool? isInternal = null)
		{
			var generated = false;

			if (isInternal is null)
			{
				while (GetKeys(KeyState.Clean, true).Count() < howMany)
				{
					GenerateNewKey("", KeyState.Clean, true, toFile: false);
					generated = true;
				}
				while (GetKeys(KeyState.Clean, false).Count() < howMany)
				{
					GenerateNewKey("", KeyState.Clean, false, toFile: false);
					generated = true;
				}
			}
			else
			{
				while (GetKeys(KeyState.Clean, isInternal).Count() < howMany)
				{
					GenerateNewKey("", KeyState.Clean, (bool)isInternal, toFile: false);
					generated = true;
				}
			}

			if (generated)
			{
				ToFile();
			}

			return generated;
		}

		/// <summary>
		/// Make sure there's always locked internal keys generated and indexed.
		/// </summary>
		public bool AssertLockedInternalKeysIndexed(int howMany = 14)
		{
			var generated = false;

			while (GetKeys(KeyState.Locked, true).Count() < howMany)
			{
				GenerateNewKey("", KeyState.Locked, true, toFile: false);
				generated = true;
			}

			if (generated)
			{
				ToFile();
			}

			return generated;
		}

		#region BlockchainState

		public Height GetBestHeight()
		{
			Height res;
			lock (BlockchainStateLock)
			{
				res = BlockchainState.BestHeight;
			}
			return res;
		}

		public IEnumerable<BlockState> GetTransactionIndex()
		{
			IEnumerable<BlockState> res = null;
			lock (BlockchainStateLock)
			{
				res = BlockchainState.BlockStates.ToList();
			}
			return res;
		}

		/// <returns>Removed element.</returns>
		public BlockState TryRemoveBlockState(uint256 blockHash)
		{
			BlockState found = null;
			lock (BlockchainStateLock)
			{
				found = BlockchainState.BlockStates.FirstOrDefault(x => x.BlockHash == blockHash);
				if (found != null)
				{
					if (BlockchainState.BlockStates.Remove(found))
					{
						ToFileNoBlockchainStateLock();
					}
				}
			}
			return found;
		}

		public bool CointainsBlockState(uint256 blockHash)
		{
			bool res = false;
			lock (BlockchainStateLock)
			{
				res = BlockchainState.BlockStates.Any(x => x.BlockHash == blockHash);
			}
			return res;
		}

		public void AddBlockState(BlockState state, bool setItsHeightToBest)
		{
			lock (BlockchainStateLock)
			{
				// Make sure of proper ordering here.

				// If found same hash then update.
				// Else If found same height then replace.
				// Else add.
				// Note same hash diff height makes no sense.

				BlockState foundWithHash = BlockchainState.BlockStates.FirstOrDefault(x => x.BlockHash == state.BlockHash);
				if (foundWithHash != null)
				{
					IEnumerable<int> newIndices = state.TransactionIndices.Where(x => !foundWithHash.TransactionIndices.Contains(x));
					if (newIndices.Any())
					{
						foundWithHash.TransactionIndices.AddRange(newIndices);
						foundWithHash.TransactionIndices.Sort();
					}
				}
				else
				{
					BlockState foundWithHeight = BlockchainState.BlockStates.FirstOrDefault(x => x.BlockHeight == state.BlockHeight);
					if (foundWithHeight != null)
					{
						BlockchainState.BlockStates.Remove(foundWithHeight);
					}

					BlockchainState.BlockStates.Add(state);
					BlockchainState.BlockStates.Sort();
				}

				if (setItsHeightToBest)
				{
					BlockchainState.BestHeight = state.BlockHeight;
				}

				ToFileNoBlockchainStateLock();
			}
		}

		public void SetBestHeight(Height height)
		{
			lock (BlockchainStateLock)
			{
				BlockchainState.BestHeight = height;
				ToFileNoBlockchainStateLock();
			}
		}

		/// <returns>The network the keymanager was used the last time on.</returns>
		public void AssertNetworkOrClearBlockstate(Network expectednetwork)
		{
			lock (BlockchainStateLock)
			{
				var lastNetwork = BlockchainState.Network;
				if (lastNetwork is null || lastNetwork != expectednetwork)
				{
					BlockchainState.Network = expectednetwork;
					BlockchainState.BestHeight = 0;
					BlockchainState.BlockStates.Clear();
					ToFileNoBlockchainStateLock();

					if (lastNetwork != null)
					{
						Logger.LogWarning<KeyManager>($"Wallet is opened on {expectednetwork.ToString()}. Last time it was opened on {lastNetwork.ToString()}.");
					}
					Logger.LogInfo<KeyManager>("Blockchain cache is cleared.");
				}
			}
		}

		#endregion BlockchainState
	}
}
