using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Input;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EasyCon.Core;

namespace EC.Avalonia.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    [ObservableProperty]
    public partial ObservableCollection<string> Items { get; set; }

    [ObservableProperty]
    public partial string? SelectedItem { get; set; }

    public MainWindowViewModel()
    {
        Items = new ObservableCollection<string>(GetItems());
    }

    private List<string> GetItems()
    {
        return ECCore.GetCaptureSources();
    }

    [RelayCommand]
    private void Refresh()
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
