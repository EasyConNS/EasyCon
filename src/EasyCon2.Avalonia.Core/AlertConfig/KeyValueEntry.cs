using CommunityToolkit.Mvvm.ComponentModel;

namespace EasyCon2.Avalonia.Core.AlertConfig;

public partial class KeyValueEntry : ObservableObject
{
    [ObservableProperty]
    private string key = "";

    [ObservableProperty]
    private string value = "";

    public KeyValueEntry() { }

    public KeyValueEntry(string key, string value)
    {
        Key = key;
        Value = value;
    }
}