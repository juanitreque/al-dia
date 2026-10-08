using AlDia.Core;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;

namespace AlDia.Vistas;

/// <summary>Piezas comunes de la interfaz (se construye en código, sin XAML).</summary>
public static class Ui
{
    public static readonly IBrush Secundario = new SolidColorBrush(Color.Parse("#6B6B6B"));
    public static readonly IBrush Aviso = new SolidColorBrush(Color.Parse("#B35C00"));
    public static readonly IBrush Error = new SolidColorBrush(Color.Parse("#C42B1C"));
    public static readonly IBrush Acento = new SolidColorBrush(Color.Parse("#0F6CBD"));

    public static TextBlock Titulo(string texto) =>
        new() { Text = texto, FontSize = 24, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 12) };

    public static TextBlock Subtitulo(string texto) =>
        new() { Text = texto, FontSize = 15, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 14, 0, 6) };

    public static TextBlock Nota(string texto, IBrush? color = null) =>
        new() { Text = texto, Foreground = color ?? Secundario, TextWrapping = TextWrapping.Wrap, FontSize = 12 };

    public static Button Boton(string texto, Action alPulsar, bool principal = false)
    {
        var b = new Button { Content = texto };
        if (principal) b.Classes.Add("accent");
        b.Click += (_, _) => alPulsar();
        return b;
    }

    public static Button Boton(string texto, Func<Task> alPulsar, bool principal = false)
    {
        var b = new Button { Content = texto };
        if (principal) b.Classes.Add("accent");
        b.Click += async (_, _) => await alPulsar();
        return b;
    }

    public static StackPanel Fila(params Control[] hijos)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        p.Children.AddRange(hijos);
        return p;
    }

    public static WrapPanel Barra(params Control[] hijos)
    {
        var p = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        foreach (var h in hijos)
        {
            h.Margin = new Thickness(0, 0, 8, 6);
            p.Children.Add(h);
        }
        return p;
    }

    public static TextBox Texto(string valor = "", string marca = "", double ancho = double.NaN) =>
        new() { Text = valor, Watermark = marca, Width = ancho, MinWidth = 80 };

    public static TextBox Importe(decimal valor, double ancho = 110) =>
        new() { Text = AlDia.Core.Importe.Texto(valor), Watermark = "0,00", Width = ancho, TextAlignment = TextAlignment.Right };

    public static decimal Leer(TextBox t) => AlDia.Core.Importe.Parse(t.Text ?? "") ?? 0;

    public static ComboBox Opciones<T>(IEnumerable<T> items, T? seleccion, double ancho = double.NaN)
    {
        var c = new ComboBox { ItemsSource = items.ToList(), Width = ancho, MinWidth = 90 };
        c.SelectedItem = seleccion;
        return c;
    }

    public static CalendarDatePicker Fecha(DateOnly? fecha) => new()
    {
        SelectedDate = fecha?.ToDateTime(TimeOnly.MinValue),
        SelectedDateFormat = CalendarDatePickerFormat.Custom,
        CustomDateFormatString = "dd/MM/yyyy",
        Width = 140,
    };

    public static DateOnly? Leer(CalendarDatePicker p) => p.SelectedDate is { } d ? DateOnly.FromDateTime(d) : null;

    /// <summary>Formulario de dos columnas: etiqueta y control.</summary>
    public static Grid Formulario(params (string Etiqueta, Control Control)[] filas)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), RowSpacing = 8, ColumnSpacing = 12 };
        for (var i = 0; i < filas.Length; i++)
        {
            g.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var etiqueta = new TextBlock { Text = filas[i].Etiqueta, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetRow(etiqueta, i);
            Grid.SetRow(filas[i].Control, i);
            Grid.SetColumn(filas[i].Control, 1);
            filas[i].Control.HorizontalAlignment = filas[i].Control is TextBox or ComboBox && double.IsNaN(filas[i].Control.Width)
                ? HorizontalAlignment.Stretch : HorizontalAlignment.Left;
            g.Children.Add(etiqueta);
            g.Children.Add(filas[i].Control);
        }
        return g;
    }

    public static DataGrid Tabla(params (string Cabecera, string Propiedad, bool Derecha)[] columnas)
    {
        var t = new DataGrid
        {
            IsReadOnly = true, CanUserSortColumns = false, CanUserReorderColumns = false, SelectionMode = DataGridSelectionMode.Single,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal, HeadersVisibility = DataGridHeadersVisibility.Column,
            HorizontalGridLinesBrush = new SolidColorBrush(Color.Parse("#E6E6E6")),
        };
        foreach (var (cabecera, propiedad, derecha) in columnas)
        {
            var col = new DataGridTextColumn { Header = cabecera, Binding = new Binding(propiedad) };
            if (derecha)
            {
                col.CellStyleClasses.Add("derecha");
                col.Width = DataGridLength.Auto;
            }
            else col.Width = propiedad is "Cliente" or "Proveedor" or "Concepto" or "Nombre" or "Descripcion"
                ? new DataGridLength(1, DataGridLengthUnitType.Star) : DataGridLength.Auto;
            t.Columns.Add(col);
        }
        t.Styles.Add(new Style(x => x.OfType<DataGridCell>().Class("derecha"))
        {
            Setters = { new Setter(Layoutable.HorizontalAlignmentProperty, HorizontalAlignment.Right) },
        });
        return t;
    }

    public static Window? Ventana(Control c) => TopLevel.GetTopLevel(c) as Window;

    // MARK: Diálogos

    /// <summary>Diálogo con contenido y botones Aceptar/Cancelar. <paramref name="validar"/> devuelve un error o null.</summary>
    public static async Task<bool> Dialogo(Window dueño, string titulo, Control contenido, string aceptar = "Guardar",
                                           Func<string?>? validar = null, double ancho = 560)
    {
        var error = Nota("", Error);
        var ventana = new Window
        {
            Title = titulo, Width = ancho, SizeToContent = SizeToContent.Height, MaxHeight = 820,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, CanResize = true, ShowInTaskbar = false,
        };
        var ok = Boton(aceptar, () =>
        {
            var e = validar?.Invoke();
            if (e is not null) { error.Text = e; return; }
            ventana.Close(true);
        }, principal: true);
        ok.IsDefault = true;
        var cancelar = Boton("Cancelar", () => ventana.Close(false));
        cancelar.IsCancel = true;
        var botones = Fila(cancelar, ok);
        botones.HorizontalAlignment = HorizontalAlignment.Right;
        var raiz = new DockPanel { Margin = new Thickness(20) };
        DockPanel.SetDock(botones, Dock.Bottom);
        DockPanel.SetDock(error, Dock.Bottom);
        error.Margin = new Thickness(0, 10, 0, 6);
        raiz.Children.Add(botones);
        raiz.Children.Add(error);
        raiz.Children.Add(new ScrollViewer { Content = contenido, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        ventana.Content = raiz;
        return await ventana.ShowDialog<bool>(dueño);
    }

    public static async Task Mensaje(Window dueño, string titulo, string texto)
    {
        var ventana = new Window
        {
            Title = titulo, Width = 460, SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, CanResize = false, ShowInTaskbar = false,
        };
        var ok = Boton("Aceptar", () => ventana.Close(), principal: true);
        ok.IsDefault = true;
        ok.HorizontalAlignment = HorizontalAlignment.Right;
        ventana.Content = new StackPanel
        {
            Margin = new Thickness(20), Spacing = 16,
            Children = { new SelectableTextBlock { Text = texto, TextWrapping = TextWrapping.Wrap }, ok },
        };
        await ventana.ShowDialog(dueño);
    }

    public static async Task<bool> Confirmar(Window dueño, string titulo, string texto, string accion = "Eliminar") =>
        await Dialogo(dueño, titulo, new TextBlock { Text = texto, TextWrapping = TextWrapping.Wrap }, accion, ancho: 440);

    // MARK: Archivos

    public static readonly FilePickerFileType Pdf = new("PDF") { Patterns = ["*.pdf"], MimeTypes = ["application/pdf"] };
    public static readonly FilePickerFileType Documentos = new("PDF o imagen")
    {
        Patterns = ["*.pdf", "*.jpg", "*.jpeg", "*.png", "*.heic"],
    };

    public static async Task<List<string>> ElegirArchivos(Window dueño, string titulo, bool varios, params FilePickerFileType[] tipos)
    {
        var archivos = await dueño.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = titulo, AllowMultiple = varios, FileTypeFilter = tipos,
        });
        return archivos.Select(a => a.TryGetLocalPath()).OfType<string>().ToList();
    }

    public static async Task<string?> GuardarComo(Window dueño, string titulo, string nombre, string extension)
    {
        var archivo = await dueño.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = titulo, SuggestedFileName = nombre, DefaultExtension = extension, ShowOverwritePrompt = true,
        });
        return archivo?.TryGetLocalPath();
    }

    public static async Task Copiar(Control c, string texto)
    {
        if (TopLevel.GetTopLevel(c)?.Clipboard is { } portapapeles) await portapapeles.SetTextAsync(texto);
    }
}

/// <summary>Sección de la ventana principal: se vuelve a cargar cada vez que se muestra.</summary>
public abstract class Seccion : UserControl
{
    public abstract string Nombre { get; }
    public abstract void Refrescar();
    protected Window Dueño => Ui.Ventana(this)!;
}
