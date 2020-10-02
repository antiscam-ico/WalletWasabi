using ReactiveUI;

namespace WalletWasabi.Fluent.ViewModels
{
	public class AddWalletPageViewModel : NavBarItemViewModel
	{
		public AddWalletPageViewModel(IScreen screen) : base(screen)
		{
			Title = "Add Wallet";
		}
	}
}
