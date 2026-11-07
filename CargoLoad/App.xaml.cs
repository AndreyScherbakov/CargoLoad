using System.Windows;

namespace CargoLoad;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Тема по умолчанию — до создания окна, чтобы не было «пустых» ресурсов.
        Theme.Apply(true);
        base.OnStartup(e);
    }
}