using EasyCon.UI.WPF.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;

namespace EasyCon.UI.WPF.Views.Pages
{
    public partial class DashboardPage : INavigableView<DashboardViewModel>
    {
        public DashboardViewModel ViewModel { get; }

        public DashboardPage(DashboardViewModel viewModel)
        {
            ViewModel = viewModel;
            DataContext = this;

            InitializeComponent();
        }
    }
}
