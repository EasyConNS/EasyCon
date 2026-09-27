using Avalonia.Controls;
using Avalonia.Controls.Templates;
using EasyCon2.Avalonia.ViewModels;
using System;
using System.Linq;

namespace EasyCon2.Avalonia;

public class ViewLocator : IDataTemplate
{

    public Control? Build(object? data)
    {
        if (data is null)
            return null;

        var name = data.GetType().FullName!.Replace("ViewModel", "View", StringComparison.Ordinal);

        // Type.GetType 只搜索本程序集与 mscorlib，Core 程序集里的 VM（如
        // AiAgentViewModel）会解析失败；改为扫描全部已加载程序集。
        var type = Type.GetType(name)
            ?? AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType(name))
                .FirstOrDefault(t => t is not null);

        if (type != null)
        {
            var control = (Control)Activator.CreateInstance(type)!;
            control.DataContext = data;
            return control;
        }

        return new TextBlock { Text = "Not Found: " + name };
    }

    public bool Match(object? data)
    {
        return data is ViewModelBase;
    }
}