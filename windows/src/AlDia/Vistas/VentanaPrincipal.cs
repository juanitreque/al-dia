using AlDia.Datos;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace AlDia.Vistas;

public sealed class VentanaPrincipal : Window
{
    private readonly ContentControl contenido = new();
    private readonly ListBox menu;
    private readonly List<Seccion> secciones;

    public VentanaPrincipal()
    {
        Title = Demo.Activo ? "Al Día · DEMO" : "Al Día";
        Width = 1180;
        Height = 760;
        MinWidth = 820;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        try { Icon = new WindowIcon(Avalonia.Platform.AssetLoader.Open(new Uri("avares://AlDia/Recursos/AlDia.ico"))); }
        catch (Exception) { /* sin icono */ }

        secciones = [new PanelVista(IrA), new IngresosVista(), new GastosVista(), new ModelosVista(), new ClientesVista(),
                     new ServiciosVista(), new AjustesVista()];
        menu = new ListBox
        {
            ItemsSource = secciones.Select(s => s.Nombre).ToList(),
            Background = Brushes.Transparent,
            Margin = new Thickness(8, 4),
        };
        menu.SelectionChanged += (_, _) => Mostrar(menu.SelectedIndex);

        var marca = new TextBlock
        {
            Text = "Al Día", FontSize = 20, FontWeight = FontWeight.Bold, Margin = new Thickness(18, 20, 18, 12),
        };
        var lateral = new DockPanel { Width = 210, Background = new SolidColorBrush(Color.Parse("#F3F3F3")) };
        DockPanel.SetDock(marca, Dock.Top);
        lateral.Children.Add(marca);
        if (Demo.Activo)
        {
            var aviso = Ui.Nota("Modo demostración: datos ficticios que se borran al cerrar.", Ui.Aviso);
            aviso.Margin = new Thickness(18, 0, 18, 16);
            DockPanel.SetDock(aviso, Dock.Bottom);
            lateral.Children.Add(aviso);
        }
        lateral.Children.Add(menu);

        contenido.Margin = new Thickness(28, 22, 28, 22);
        contenido.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        contenido.VerticalContentAlignment = VerticalAlignment.Stretch;
        var raiz = new DockPanel();
        DockPanel.SetDock(lateral, Dock.Left);
        raiz.Children.Add(lateral);
        raiz.Children.Add(contenido);
        Content = raiz;

        menu.SelectedIndex = 0;
    }

    private void IrA(Type seccion) => menu.SelectedIndex = secciones.FindIndex(s => s.GetType() == seccion);

    private void Mostrar(int indice)
    {
        if (indice < 0) return;
        var s = secciones[indice];
        s.Refrescar();
        contenido.Content = s;
    }
}
