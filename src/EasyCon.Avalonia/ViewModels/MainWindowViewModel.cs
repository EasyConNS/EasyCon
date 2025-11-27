using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasyCon.Core;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;

namespace EC.Avalonia.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
#pragma warning disable CA1822 // Mark members as static
    public string Greeting => "Welcome to EasyCon2!";
    public ICommand RefreshCommand { get; }
#pragma warning restore CA1822 // Mark members as static

    [ObservableProperty]
    private ObservableCollection<string> _items;

    [ObservableProperty]
    private string? _selectedItem;

    public MainWindowViewModel()
    {
        Items = new ObservableCollection<string>(GetItems());
        RefreshCommand = new RelayCommand(LoadItems);
    }

    private List<string> GetItems()
    {
        return ECCore.GetCaptureSources();
    }

    private void LoadItems()
    {
        Items.Clear();
        foreach (var item in GetItems())
        {
            Items.Add(item);
        }
    }

    partial void OnSelectedItemChanged(string? value)
    {
        Debug.WriteLine($"选中id：{value}");
    }
}
