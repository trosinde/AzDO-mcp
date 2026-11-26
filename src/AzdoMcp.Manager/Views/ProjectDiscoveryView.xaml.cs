using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using AzdoMcp.Manager.Core.ViewModels;

namespace AzdoMcp.Manager.Views;

public partial class ProjectDiscoveryView : UserControl
{
    public ProjectDiscoveryView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is INotifyPropertyChanged old)
            {
                old.PropertyChanged -= OnVmChanged;
            }

            if (e.NewValue is INotifyPropertyChanged now)
            {
                now.PropertyChanged += OnVmChanged;
            }
        };
    }

    private SetupWizardViewModel? Vm => DataContext as SetupWizardViewModel;

    // PasswordBox.Password is not bindable by design; the PAT only flows view → view model.
    private void OnPatChanged(object sender, RoutedEventArgs e)
    {
        if (Vm is { } vm && vm.Pat != Pat.Password)
        {
            vm.Pat = Pat.Password;
        }
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SetupWizardViewModel.Pat) && Vm is { Pat: "" } && Pat.Password.Length > 0)
        {
            Pat.Clear();
        }
    }
}
