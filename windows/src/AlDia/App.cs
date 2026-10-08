using AlDia.Vistas;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Themes.Fluent;

namespace AlDia;

public sealed class App : Application
{
    public override void Initialize()
    {
        Name = "Al Día";
        Styles.Add(new FluentTheme());
        Styles.Add(new StyleInclude(new Uri("avares://AlDia/"))
        {
            Source = new Uri("avares://Avalonia.Controls.DataGrid/Themes/Fluent.xaml"),
        });
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime escritorio)
            escritorio.MainWindow = new VentanaPrincipal();
        base.OnFrameworkInitializationCompleted();
    }
}
