using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using AzdoMcp.Core.Connections;
using AzdoMcp.Manager.Core.Mvvm;
using AzdoMcp.Manager.Core.Services;
using Microsoft.Win32;

namespace AzdoMcp.Manager.Platform;

public sealed class WpfDispatcher(Dispatcher dispatcher) : IUiDispatcher
{
    public void Post(Action action)
    {
        if (dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.BeginInvoke(action);
        }
    }
}

/// <summary>HKCU\Software\Microsoft\Windows\CurrentVersion\Run – no admin rights needed.</summary>
public sealed class WindowsRunKey : IRunKey
{
    private const string Path = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public string? Get(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(Path);
        return key?.GetValue(name) as string;
    }

    public void Set(string name, string value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(Path);
        key.SetValue(name, value);
    }

    public void Delete(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(Path, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }
}

public sealed class WpfDialogs : IDialogs
{
    private static Window? Owner => Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive && w.IsVisible);

    public bool Confirm(string title, string message) =>
        Show(message, title, MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK;

    public bool? AskSaveChanges(string message) => Show(message, "Ungespeicherte Änderungen", MessageBoxButton.YesNoCancel, MessageBoxImage.Question) switch
    {
        MessageBoxResult.Yes => true,
        MessageBoxResult.No => false,
        _ => null,
    };

    public void Info(string title, string message) => Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    public void Error(string title, string message) => Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);

    public string? SaveFile(string title, string defaultName, string filter)
    {
        var dlg = new SaveFileDialog { Title = title, FileName = defaultName, Filter = filter };
        return dlg.ShowDialog(Owner) == true ? dlg.FileName : null;
    }

    public string? PickFolder(string title)
    {
        var dlg = new OpenFolderDialog { Title = title };
        return dlg.ShowDialog(Owner) == true ? dlg.FolderName : null;
    }

    public void OpenUrl(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    public void OpenFolder(string path)
    {
        System.IO.Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { path }, UseShellExecute = false });
    }

    public void CopyText(string text) => Clipboard.SetText(text);

    private static MessageBoxResult Show(string message, string title, MessageBoxButton buttons, MessageBoxImage image) =>
        Owner is { } o
            ? MessageBox.Show(o, message, title, buttons, image)
            : MessageBox.Show(message, title, buttons, image);
}

internal static class Tones
{
    public static Brush Brush(string key) => (Brush)Application.Current.Resources[key];
}

/// <summary>"g"/"y"/"r"/"n" → pill background.</summary>
public sealed class ToneBackgroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => (value as string) switch
    {
        "g" => Tones.Brush("OkBg"),
        "y" => Tones.Brush("WarnBg"),
        "r" => Tones.Brush("ErrBg"),
        _ => Tones.Brush("NeutralBg"),
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class ToneForegroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => (value as string) switch
    {
        "g" => Tones.Brush("OkFg"),
        "y" => Tones.Brush("WarnFg"),
        "r" => Tones.Brush("ErrFg"),
        _ => Tones.Brush("NeutralFg"),
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Tone string or <see cref="StatusLevel"/> → dot colour.</summary>
public sealed class LevelDotConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        "g" or StatusLevel.Ok => Tones.Brush("OkDot"),
        "y" or StatusLevel.Warning => Tones.Brush("WarnDot"),
        "r" or StatusLevel.Error => Tones.Brush("ErrDot"),
        _ => Tones.Brush("NeutralFg"),
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is null || (value is string s && s.Length == 0) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Indent (DIP) → left margin.</summary>
public sealed class IndentConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        new Thickness(value is double d ? d : 0, 0, 0, 0);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Inherited mode → faded switch.</summary>
public sealed class InheritedOpacityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is true ? 0.55 : 1.0;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
