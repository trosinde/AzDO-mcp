using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using AzdoMcp.Manager.Core.ViewModels;

namespace AzdoMcp.Manager.Views;

public partial class ConnectionsView : UserControl
{
    public ConnectionsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private ConnectionsViewModel? Vm => DataContext as ConnectionsViewModel;

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is INotifyPropertyChanged old)
        {
            old.PropertyChanged -= OnVmChanged;
        }

        if (e.NewValue is INotifyPropertyChanged now)
        {
            now.PropertyChanged += OnVmChanged;
        }
    }

    // PasswordBox.Password is not bindable (by design); the PAT only flows view → view model.
    private void OnPatChanged(object sender, RoutedEventArgs e)
    {
        if (Vm is { } vm && vm.NewPat != Pat.Password)
        {
            vm.NewPat = Pat.Password;
        }
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ConnectionsViewModel.NewPat) or "" && Vm is { NewPat: "" } && Pat.Password.Length > 0)
        {
            Pat.Clear();
        }
    }
}
