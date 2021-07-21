using NBitcoin;
using ReactiveUI;
using WalletWasabi.Blockchain.TransactionBuilding;
using WalletWasabi.Fluent.Helpers;
using WalletWasabi.Fluent.Models;
using WalletWasabi.Fluent.ViewModels.Dialogs.Base;

namespace WalletWasabi.Fluent.ViewModels.Dialogs
{
	[NavigationMetaData(Title = "Insufficient Balance")]
	public partial class InsufficientBalanceDialogViewModel : DialogViewModelBase<bool>
	{
		public InsufficientBalanceDialogViewModel(BalanceType type, BuildTransactionResult transaction, decimal usdExchangeRate)
		{
			var destinationAmount = transaction.CalculateDestinationAmount().ToDecimal(MoneyUnit.BTC);
			var btcAmountText = $"{destinationAmount} bitcoins ";
			var fiatAmountText = destinationAmount.GenerateFiatText(usdExchangeRate, "USD");
			AmountText = $"{btcAmountText}{fiatAmountText}";

			var fee = transaction.Fee;
			var btcFeeText = $"{fee.ToDecimal(MoneyUnit.Satoshi)} sats ";
			var fiatFeeText = fee.ToDecimal(MoneyUnit.BTC).GenerateFiatText(usdExchangeRate, "USD");
			FeeText = $"{btcFeeText}{fiatFeeText}";

			Caption = type switch
			{
				BalanceType.Private => $"There are not enough private funds to cover the transaction fee. Alternatively you could:",
				BalanceType.Pocket => $"There are not enough funds selected to cover the transaction fee. Alternatively you could:",
				_ => $"There are not enough funds available to cover the transaction fee. Alternatively you could:",
			};

			NextCommand = ReactiveCommand.Create(() => Close(result: true));
			CancelCommand = ReactiveCommand.Create(() => Close(DialogResultKind.Cancel));

			SetupCancel(enableCancel: false, enableCancelOnEscape: true, enableCancelOnPressed: true);
		}

		public string AmountText { get; set; }

		public string FeeText { get; set; }

		public string Caption { get; }
	}
}
