using AlDia.Core;
using AlDia.Datos;
using AlDia.Servicios;
using Avalonia;
using Avalonia.Controls;

namespace AlDia.Vistas;

public sealed class FilaGasto(Gasto g)
{
    public Gasto Gasto => g;
    public string Fecha => Formato.Fecha(g.Fecha);
    public string Proveedor => g.Proveedor;
    public string Concepto => g.Concepto;
    public string Categoria => g.Categoria;
    public string Base => Formato.Euros(g.Base);
    public string Iva => Formato.Euros(g.CuotaIVA);
    public string Deducible => Formato.Euros(g.Datos.IvaDeducible);
    public string Total => Formato.Euros(g.Total);
    public string Documento => g.TieneDocumento ? "Sí" : "";
}

public sealed class GastosVista : Seccion
{
    public override string Nombre => "Gastos";

    private readonly DataGrid tabla = Ui.Tabla(("Fecha", "Fecha", false), ("Proveedor", "Proveedor", false), ("Concepto", "Concepto", false),
        ("Categoría", "Categoria", false), ("Base", "Base", true), ("IVA", "Iva", true), ("IVA deducible", "Deducible", true),
        ("Total", "Total", true), ("Doc.", "Documento", false));
    private readonly ComboBox filtro = new() { MinWidth = 140 };
    private readonly TextBlock totales = Ui.Nota("");
    private List<Gasto> todos = [];

    public GastosVista()
    {
        tabla.DoubleTapped += async (_, _) => { if (Seleccionado is { } g) await Editar(g); };
        filtro.SelectionChanged += (_, _) => Llenar();
        var barra = Ui.Barra(
            Ui.Boton("Nuevo gasto", () => Editar(new Gasto()), principal: true),
            Ui.Boton("Importar factura o ticket…", Importar),
            Ui.Boton("Editar", () => Seleccionado is { } g ? Editar(g) : Task.CompletedTask),
            Ui.Boton("Ver documento", VerDocumento),
            Ui.Boton("Eliminar", Eliminar),
            Ui.Boton("Exportar CSV…", ExportarCsv),
            filtro);
        var raiz = new DockPanel();
        var cabecera = new StackPanel { Children = { Ui.Titulo("Gastos"), barra } };
        DockPanel.SetDock(cabecera, Dock.Top);
        DockPanel.SetDock(totales, Dock.Bottom);
        totales.Margin = new Thickness(0, 10, 0, 0);
        raiz.Children.Add(cabecera);
        raiz.Children.Add(totales);
        raiz.Children.Add(tabla);
        Content = raiz;
    }

    private Gasto? Seleccionado => (tabla.SelectedItem as FilaGasto)?.Gasto;

    public override void Refrescar()
    {
        todos = Almacen.Gastos();
        var actual = filtro.SelectedItem as string;
        var opciones = Filtros.Opciones(todos.Select(g => g.Fecha));
        filtro.ItemsSource = opciones;
        filtro.SelectedItem = actual is not null && opciones.Contains(actual) ? actual : opciones[0];
        Llenar();
    }

    private void Llenar()
    {
        if (filtro.SelectedItem is not string f) return;
        var lista = todos.Where(g => Filtros.Contiene(f, g.Fecha)).OrderByDescending(g => g.Fecha).ToList();
        tabla.ItemsSource = lista.Select(g => new FilaGasto(g)).ToList();
        var d = lista.Select(g => g.Datos).ToList();
        totales.Text = $"{lista.Count} gasto(s) · Base {Formato.Euros(lista.Sum(g => g.Base))} · IVA deducible {Formato.Euros(d.Sum(x => x.IvaDeducible))}" +
                       $" · Gasto IRPF {Formato.Euros(d.Sum(x => x.GastoIRPF))} · Total {Formato.Euros(lista.Sum(g => g.Total))}";
    }

    private async Task Editar(Gasto g)
    {
        if (await EditorGasto.Mostrar(Dueño, g))
        {
            Almacen.Guardar(g, conAdjunto: g.Adjunto is not null);
            Refrescar();
        }
    }

    /// <summary>Lee el PDF del ticket o factura y abre el editor con los datos encontrados.</summary>
    private async Task Importar()
    {
        var archivos = await Ui.ElegirArchivos(Dueño, "Factura o ticket de compra", false, Ui.Documentos);
        if (archivos.FirstOrDefault() is not { } ruta) return;
        var datos = await File.ReadAllBytesAsync(ruta);
        var g = new Gasto { Adjunto = datos, AdjuntoNombre = Path.GetFileName(ruta), TieneDocumento = true };
        var nota = "";
        if (Path.GetExtension(ruta).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var texto = LecturaPdf.Texto(datos);
                if (texto.Trim().Length < 20)
                    nota = "El PDF no contiene texto (parece escaneado): rellena los datos a mano.";
                else
                {
                    var l = LectorTicket.Analizar(texto, Ajustes.Texto(Ajustes.Nif), Ajustes.Texto(Ajustes.Nombre));
                    g.Proveedor = l.Proveedor;
                    g.NifProveedor = l.NifProveedor;
                    g.NumeroFactura = l.NumeroFactura;
                    g.Fecha = l.Fecha ?? g.Fecha;
                    g.Base = l.Base;
                    g.TipoIVA = l.TipoIVA;
                    g.CuotaIVA = l.CuotaIVA;
                    g.FacturaCompleta = l.AMiNombre;
                    if (l.ProveedorExtranjero)
                        nota = "Proveedor de otro país de la UE: si la factura no lleva IVA español (inversión del sujeto pasivo), consúltalo con tu gestor.";
                    else if (!l.AMiNombre)
                        nota = "No aparece tu NIF: si es un ticket simplificado, su IVA no es deducible.";
                }
            }
            catch (Exception e) { nota = $"No se pudo leer el PDF ({e.Message}): rellena los datos a mano."; }
        }
        else nota = "La lectura automática de fotos aún no está disponible en Windows: rellena los datos mirando la imagen (botón «Ver documento»).";
        await Editar(g, nota);
    }

    private async Task Editar(Gasto g, string nota)
    {
        if (await EditorGasto.Mostrar(Dueño, g, nota))
        {
            Almacen.Guardar(g, conAdjunto: true);
            Refrescar();
        }
    }

    private async Task VerDocumento()
    {
        if (Seleccionado is not { TieneDocumento: true } g) return;
        if (Almacen.AdjuntoGasto(g.Id) is not { Length: > 0 } datos) return;
        var ruta = Sistema.Temporal(Formato.NombreArchivo(g.AdjuntoNombre ?? "Documento.pdf"));
        await File.WriteAllBytesAsync(ruta, datos);
        Sistema.Abrir(ruta);
    }

    private async Task Eliminar()
    {
        if (Seleccionado is not { } g) return;
        if (await Ui.Confirmar(Dueño, "Eliminar gasto", $"¿Eliminar el gasto de {g.Proveedor} del {Formato.Fecha(g.Fecha)}?"))
        {
            Almacen.Borrar(g);
            Refrescar();
        }
    }

    private async Task ExportarCsv()
    {
        var f = filtro.SelectedItem as string ?? "";
        if (await Ui.GuardarComo(Dueño, "Libro de gastos", Formato.NombreArchivo($"Gastos {f}.csv"), "csv") is { } ruta)
            await File.WriteAllTextAsync(ruta, Libros.Gastos(todos.Where(g => Filtros.Contiene(f, g.Fecha))), new System.Text.UTF8Encoding(false));
    }
}

public static class EditorGasto
{
    private static readonly decimal[] TiposIva = [0, 4, 5, 10, 21];

    public static async Task<bool> Mostrar(Window dueño, Gasto g, string nota = "")
    {
        var fecha = Ui.Fecha(g.Fecha);
        var proveedor = Ui.Texto(g.Proveedor, "Nombre del proveedor");
        var nif = Ui.Texto(g.NifProveedor, "B12345678", 160);
        var numero = Ui.Texto(g.NumeroFactura, "Nº de su factura", 200);
        var concepto = Ui.Texto(g.Concepto, "Qué has comprado");
        var categoria = Ui.Opciones(Categorias.Todas, Categorias.Todas.Contains(g.Categoria) ? g.Categoria : "Otros", 260);
        var @base = Ui.Importe(g.Base);
        var iva = Ui.Opciones(TiposIva.Union([g.TipoIVA]).Order().Select(Formato.Porcentaje).ToList(), Formato.Porcentaje(g.TipoIVA), 110);
        var cuota = Ui.Importe(g.CuotaIVA);
        var deducible = Ui.Opciones(["100 %", "50 %", "0 %"], Formato.Porcentaje(g.PorcentajeDeducibleIVA), 110);
        var completa = new CheckBox { Content = "Factura completa a mi nombre (con mi NIF): el IVA es deducible", IsChecked = g.FacturaCompleta };
        var irpf = new CheckBox { Content = "Deducible en el IRPF (130 y renta)", IsChecked = g.DeducibleIRPF };
        var inversion = new CheckBox { Content = "Bien de inversión (más de 300 € y dura más de un año: se amortiza)", IsChecked = g.BienInversion };
        var notas = Ui.Texto(g.Notas, "Notas");
        var total = new TextBlock { FontWeight = Avalonia.Media.FontWeight.SemiBold };
        var documento = Ui.Nota(g.TieneDocumento ? $"Documento: {g.AdjuntoNombre}" : "Sin documento adjunto");
        decimal Tipo(ComboBox c) => decimal.Parse(((string)c.SelectedItem!).Replace(" %", ""), Formato.Es);

        void Total() => total.Text = $"Total {Formato.Euros(Ui.Leer(@base) + Ui.Leer(cuota))}";
        void Cuota() { cuota.Text = AlDia.Core.Importe.Texto(Calculo.Porcentaje(Tipo(iva), Ui.Leer(@base))); Total(); }
        @base.TextChanged += (_, _) => Cuota();
        iva.SelectionChanged += (_, _) => Cuota();
        cuota.TextChanged += (_, _) => Total();
        categoria.SelectionChanged += (_, _) =>
        {
            if (g.Id != 0 || Categorias.Sugerencia((string)categoria.SelectedItem!) is not { } s) return;
            iva.SelectedItem = Formato.Porcentaje(s.Iva);
            deducible.SelectedItem = Formato.Porcentaje(s.Deducible);
        };
        Total();

        var adjuntar = Ui.Boton(g.TieneDocumento ? "Cambiar documento…" : "Adjuntar documento…", async () =>
        {
            var archivos = await Ui.ElegirArchivos(dueño, "Documento del gasto", false, Ui.Documentos);
            if (archivos.FirstOrDefault() is not { } ruta) return;
            g.Adjunto = await File.ReadAllBytesAsync(ruta);
            g.AdjuntoNombre = Path.GetFileName(ruta);
            g.TieneDocumento = true;
            documento.Text = $"Documento: {g.AdjuntoNombre}";
        });

        var contenido = new StackPanel { Spacing = 10 };
        if (nota.Length > 0) contenido.Children.Add(Ui.Nota(nota, Ui.Aviso));
        contenido.Children.Add(Ui.Formulario(("Fecha", fecha), ("Proveedor", proveedor), ("NIF proveedor", nif), ("Nº factura", numero),
            ("Concepto", concepto), ("Categoría", categoria), ("Base imponible", @base), ("Tipo de IVA", iva), ("Cuota de IVA", cuota),
            ("% IVA deducible", deducible)));
        contenido.Children.Add(total);
        contenido.Children.Add(completa);
        contenido.Children.Add(irpf);
        contenido.Children.Add(inversion);
        contenido.Children.Add(Ui.Formulario(("Notas", notas)));
        contenido.Children.Add(Ui.Fila(adjuntar, documento));

        string? Validar() => Ui.Leer(fecha) is null ? "Indica la fecha." : string.IsNullOrWhiteSpace(proveedor.Text) ? "Indica el proveedor." : null;
        if (!await Ui.Dialogo(dueño, g.Id == 0 ? "Nuevo gasto" : "Gasto", contenido, validar: Validar, ancho: 640))
            return false;

        g.Fecha = Ui.Leer(fecha)!.Value;
        g.Proveedor = proveedor.Text!.Trim();
        g.NifProveedor = (nif.Text ?? "").Trim().ToUpperInvariant();
        g.NumeroFactura = (numero.Text ?? "").Trim();
        g.Concepto = (concepto.Text ?? "").Trim();
        g.Categoria = (string)categoria.SelectedItem!;
        g.Base = Ui.Leer(@base);
        g.TipoIVA = Tipo(iva);
        g.CuotaIVA = Ui.Leer(cuota);
        g.PorcentajeDeducibleIVA = Tipo(deducible);
        g.FacturaCompleta = completa.IsChecked == true;
        g.DeducibleIRPF = irpf.IsChecked == true;
        g.BienInversion = inversion.IsChecked == true;
        g.Notas = (notas.Text ?? "").Trim();
        return true;
    }
}
