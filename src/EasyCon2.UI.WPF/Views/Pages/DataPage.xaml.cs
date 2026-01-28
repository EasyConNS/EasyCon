using EasyCon.UI.WPF.ViewModels.Pages;
using Wpf.Ui.Abstractions.Controls;

namespace EasyCon.UI.WPF.Views.Pages
{
    public partial class DataPage : INavigableView<DataViewModel>
    {
        public DataViewModel ViewModel { get; }

        public DataPage(DataViewModel viewModel)
        {
            ViewModel = viewModel;
            DataContext = this;

            InitializeComponent();
        }
    }
}
