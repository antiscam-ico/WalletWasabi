using NBitcoin;
using ReactiveUI;
using System.Collections.Generic;
using System.Windows.Input;
using WalletWasabi.Blockchain.Keys;
using WalletWasabi.Wallets;

namespace WalletWasabi.Fluent.ViewModels.AddWallet
{
	public class RecoveryWordsViewModel : RoutableViewModel
	{
		public RecoveryWordsViewModel(
			IScreen screen,
			KeyManager keyManager,
			Mnemonic mnemonic,
			WalletManager walletManager)
			: base(screen)
		{
			MnemonicWords = new List<RecoveryWordViewModel>();

			for (int i = 0; i < mnemonic.Words.Length; i++)
			{
				MnemonicWords.Add(new RecoveryWordViewModel(i + 1, mnemonic.Words[i]));
			}

			ContinueCommand = ReactiveCommand.Create(
				() => screen.Router.Navigate.Execute(
					new ConfirmRecoveryWordsViewModel(HostScreen, MnemonicWords, keyManager, walletManager)));
		}

		public ICommand ContinueCommand { get; }

		public List<RecoveryWordViewModel> MnemonicWords { get; set; }
	}
}