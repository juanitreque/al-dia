using AlDia.Core;
using AlDia.Datos;
using AlDia.Servicios;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace AlDia.Vistas;

public sealed class FilaIngreso(Ingreso i)
{
    public Ingreso Ingreso => i;
    public string Fecha => Formato.Fecha(i.Fecha);
    public string Numero => i.Numero;
    public string Cliente => i.Cliente?.Nombre ?? "";
    public string Base => Formato.Euros(i.Base);
    public string Iva => Formato.Euros(i.CuotaIVA);
    public string Retencion => i.Retencion == 0 ? "" : Formato.Euros(-i.Retencion);
    public string Total => Formato.Euros(i.Total);
    public string Estado => i.Borrador ? "Borrador" : i.Cobrada ? "Cobrada" : "Pendiente de cobro";
    public string Pdf => i.TieneDocumento ? "Sí" : "";
}

public sealed class IngresosVista : Seccion
{
    public override string Nombre => "Ingresos";

    private readonly DataGrid tabla = Ui.Tabla(("Fecha", "Fecha", false), ("Número", "Numero", false), ("Cliente", "Cliente", false),
        ("Base", "Base", true), ("IVA", "Iva", true), ("Retención", "Retencion", true), ("Total", "Total", true),
        ("Estado", "Estado", false), ("PDF", "Pdf", false));
    private readonly ComboBox filtro = new() { MinWidth = 140 };
    private readonly TextBlock totales = Ui.Nota("");
    private List<Ingreso> todos = [];

    public IngresosVista()
    {
        tabla.DoubleTapped += async (_, _) => await AbrirSeleccionada();
        filtro.SelectionChanged += (_, _) => Llenar();
        var barra = Ui.Barra(
            Ui.Boton("Nueva factura", () => Editar(null), principal: true),
            Ui.Boton("Editar", () => Seleccionada is { } s ? Editar(s) : Task.CompletedTask),
            Ui.Boton("Emitir PDF", Emitir),
            Ui.Boton("Ver PDF", VerPdf),
            Ui.Boton("Enviar por correo", Enviar),
            Ui.Boton("Marcar cobrada", MarcarCobrada),
            Ui.Boton("Eliminar", Eliminar),
            Ui.Boton("Importar facturas PDF…", Importar),
            Ui.Boton("Exportar CSV…", ExportarCsv),
            filtro);
        var raiz = new DockPanel();
        var cabecera = new StackPanel { Children = { Ui.Titulo("Ingresos"), barra } };
        DockPanel.SetDock(cabecera, Dock.Top);
        DockPanel.SetDock(totales, Dock.Bottom);
        totales.Margin = new Thickness(0, 10, 0, 0);
        raiz.Children.Add(cabecera);
        raiz.Children.Add(totales);
        raiz.Children.Add(tabla);
        Content = raiz;
    }

    private Ingreso? Seleccionada => (tabla.SelectedItem as FilaIngreso)?.Ingreso;

    public override void Refrescar()
    {
        todos = Almacen.Ingresos();
        var actual = filtro.SelectedItem as string;
        var opciones = Filtros.Opciones(todos.Select(i => i.Fecha));
        filtro.ItemsSource = opciones;
        filtro.SelectedItem = actual is not null && opciones.Contains(actual) ? actual : opciones[0];
        Llenar();
    }

    private void Llenar()
    {
        if (filtro.SelectedItem is not string f) return;
        var lista = todos.Where(i => Filtros.Contiene(f, i.Fecha)).OrderByDescending(i => i.Fecha).ThenByDescending(i => i.Numero).ToList();
        tabla.ItemsSource = lista.Select(i => new FilaIngreso(i)).ToList();
        var emitidas = lista.Where(i => !i.Borrador).ToList();
        totales.Text = $"{emitidas.Count} emitida(s) · Base {Formato.Euros(emitidas.Sum(i => i.Base))} · IVA {Formato.Euros(emitidas.Sum(i => i.CuotaIVA))}" +
                       $" · Retenciones {Formato.Euros(emitidas.Sum(i => i.Retencion))} · Total {Formato.Euros(emitidas.Sum(i => i.Total))}" +
                       (lista.Count > emitidas.Count ? $" · {lista.Count - emitidas.Count} borrador(es), que no cuentan" : "");
    }

    private async Task AbrirSeleccionada()
    {
        if (Seleccionada is not { } i) return;
        if (i.Borrador) await Editar(i); else await VerPdf();
    }

    private async Task Editar(Ingreso? existente)
    {
        var i = existente ?? NuevaFactura();
        if (await EditorIngreso.Mostrar(Dueño, i, todos))
        {
            Almacen.Guardar(i);
            Refrescar();
        }
    }

    private Ingreso NuevaFactura()
    {
        var hoy = DateOnly.FromDateTime(DateTime.Today);
        var numeros = todos.OrderBy(x => x.Fecha).Select(x => x.Numero).ToList();
        return new Ingreso
        {
            Borrador = true, Fecha = hoy, TipoIVA = Ajustes.IvaPorDefecto, TipoRetencion = Ajustes.RetencionPorDefecto,
            Numero = Ajustes.NumeroSugerido(hoy, numeros, numeros),
            Lineas = [new LineaIngreso()],
        };
    }

    /// <summary>Emite el borrador: comprueba los datos obligatorios, genera el PDF, lo guarda y lo archiva.</summary>
    private async Task Emitir()
    {
        if (Seleccionada is not { } i) { await Ui.Mensaje(Dueño, "Emitir", "Selecciona primero una factura en borrador."); return; }
        if (!i.Borrador && !await Ui.Confirmar(Dueño, "Volver a generar",
                "Esta factura ya está emitida. ¿Generar de nuevo su PDF con los datos actuales? Sustituye el documento guardado.", "Generar"))
            return;
        var emisor = new Emisor();
        var faltan = FacturaPdf.DatosQueFaltan(i, emisor);
        if (faltan.Count > 0)
        {
            await Ui.Mensaje(Dueño, "Faltan datos", "Para emitir la factura falta:\n\n• " + string.Join("\n• ", faltan));
            return;
        }
        if (VeriFactu.Obliga(i.Fecha) && !await Ui.Confirmar(Dueño, "VeriFactu",
                $"Desde el {Formato.Fecha(VeriFactu.ObligatorioDesde)} las facturas hechas con programas deben cumplir VeriFactu, " +
                "y Al Día no envía registros a la AEAT. Emite esta factura en la app gratuita de la AEAT. ¿Generar el PDF igualmente?", "Generar"))
            return;
        var duplicado = todos.FirstOrDefault(x => x.Id != i.Id && !x.Borrador && x.Numero == i.Numero);
        if (duplicado is not null)
        {
            await Ui.Mensaje(Dueño, "Número repetido", $"Ya hay otra factura emitida con el número {i.Numero}. Cámbialo antes de emitir.");
            return;
        }
        var pdf = FacturaPdf.Generar(i, emisor);
        i.Borrador = false;
        i.Adjunto = pdf;
        i.AdjuntoNombre = FacturaPdf.NombreArchivo(i);
        i.TieneDocumento = true;
        Almacen.Guardar(i, conAdjunto: true);
        string copia;
        try { copia = FacturaPdf.GuardarCopia(pdf, i); }
        catch (Exception e) { await Ui.Mensaje(Dueño, "Factura emitida", $"Factura emitida, pero no se pudo guardar la copia: {e.Message}"); Refrescar(); return; }
        Refrescar();
        Sistema.Abrir(copia);
        await Ui.Mensaje(Dueño, "Factura emitida", $"La factura {i.Numero} está emitida. Copia guardada en:\n{copia}");
    }

    private async Task<string?> PdfDe(Ingreso i)
    {
        if (i.Borrador)
        {
            var ruta = Sistema.Temporal(Path.GetFileNameWithoutExtension(FacturaPdf.NombreArchivo(i)) + " (borrador).pdf");
            await File.WriteAllBytesAsync(ruta, FacturaPdf.Generar(i, borrador: true));
            return ruta;
        }
        var datos = i.TieneDocumento ? Almacen.AdjuntoIngreso(i.Id) : null;
        var nombre = i.AdjuntoNombre is { Length: > 0 } n ? Formato.NombreArchivo(n) : FacturaPdf.NombreArchivo(i);
        var destino = Sistema.Temporal(nombre);
        await File.WriteAllBytesAsync(destino, datos is { Length: > 0 } ? datos : FacturaPdf.Generar(i));
        return destino;
    }

    private async Task VerPdf()
    {
        if (Seleccionada is not { } i) return;
        if (await PdfDe(i) is { } ruta) Sistema.Abrir(ruta);
    }

    private async Task Enviar()
    {
        if (Seleccionada is not { } i) return;
        if (i.Borrador) { await Ui.Mensaje(Dueño, "Enviar", "Emite la factura antes de enviarla."); return; }
        if (await PdfDe(i) is not { } ruta) return;
        var mes = i.Fecha.ToString("MMMM yyyy", Formato.Es);
        var asunto = $"Factura {i.Numero} · {mes}";
        var cuerpo = $"Hola:\n\nTe envío la factura {i.Numero}, por un total de {Formato.Euros(i.Total)}.\n\nUn saludo,\n{Ajustes.Texto(Ajustes.Nombre)}";
        await DialogoCorreo.Enviar(Dueño, i.Cliente?.Email ?? "", asunto, cuerpo, ruta);
    }

    private void MarcarCobrada()
    {
        if (Seleccionada is not { Borrador: false } i) return;
        i.Cobrada = !i.Cobrada;
        i.FechaCobro = i.Cobrada ? DateOnly.FromDateTime(DateTime.Today) : null;
        Almacen.Guardar(i);
        Refrescar();
    }

    private async Task Eliminar()
    {
        if (Seleccionada is not { } i) return;
        if (await Ui.Confirmar(Dueño, "Eliminar factura", $"¿Eliminar la factura {i.Numero}? No se puede deshacer."))
        {
            Almacen.Borrar(i);
            Refrescar();
        }
    }

    /// <summary>Importa facturas propias ya emitidas (PDF con cabecera «Número: … Fecha: …»).</summary>
    private async Task Importar()
    {
        var archivos = await Ui.ElegirArchivos(Dueño, "Facturas emitidas en PDF", true, Ui.Pdf);
        if (archivos.Count == 0) return;
        var clientes = Almacen.Clientes();
        var informe = new List<string>();
        var importadas = 0;
        foreach (var ruta in archivos)
        {
            var nombre = Path.GetFileName(ruta);
            try
            {
                var datos = await File.ReadAllBytesAsync(ruta);
                if (LectorFactura.Analizar(LecturaPdf.Texto(datos)) is not { } f)
                {
                    informe.Add($"✗ {nombre}: no se reconoce el formato (se necesita «Número: … Fecha: …»).");
                    continue;
                }
                if (todos.Any(x => x.Numero == f.Numero && !x.Borrador))
                {
                    informe.Add($"– {nombre}: la factura {f.Numero} ya estaba registrada.");
                    continue;
                }
                var cliente = clientes.FirstOrDefault(c => f.ClienteNIF.Length > 0 && c.Nif.Equals(f.ClienteNIF, StringComparison.OrdinalIgnoreCase));
                if (cliente is null && f.ClienteNombre.Length > 0)
                {
                    cliente = new Cliente { Nombre = f.ClienteNombre, Nif = f.ClienteNIF };
                    Almacen.Guardar(cliente);
                    clientes.Add(cliente);
                }
                var i = new Ingreso
                {
                    Numero = f.Numero, Fecha = f.Fecha, Cliente = cliente, Base = f.Base, TipoIVA = f.TipoIVA, CuotaIVA = f.CuotaIVA,
                    TipoRetencion = f.TipoRetencion, Retencion = f.Retencion, Concepto = f.Lineas.FirstOrDefault()?.Concepto ?? "",
                    Lineas = f.Lineas.Select(l => new LineaIngreso { Concepto = l.Concepto, Cantidad = l.Cantidad, Precio = l.Precio }).ToList(),
                    Adjunto = datos, AdjuntoNombre = nombre, TieneDocumento = true,
                };
                Almacen.Guardar(i, conAdjunto: true);
                todos.Add(i);
                importadas++;
                informe.Add($"✓ {nombre}: {f.Numero} · {Formato.Euros(f.Total)}" + (f.Avisos.Count > 0 ? $" (revisar: {string.Join("; ", f.Avisos)})" : ""));
            }
            catch (Exception e)
            {
                informe.Add($"✗ {nombre}: {e.Message}");
            }
        }
        Refrescar();
        await Ui.Mensaje(Dueño, "Importación", $"{importadas} factura(s) importada(s).\n\n{string.Join("\n", informe)}");
    }

    private async Task ExportarCsv()
    {
        var f = filtro.SelectedItem as string ?? "";
        var lista = todos.Where(i => !i.Borrador && Filtros.Contiene(f, i.Fecha));
        if (await Ui.GuardarComo(Dueño, "Libro de ingresos", Formato.NombreArchivo($"Ingresos {f}.csv"), "csv") is { } ruta)
            await File.WriteAllTextAsync(ruta, Libros.Ingresos(lista), new System.Text.UTF8Encoding(false));
    }
}

/// <summary>Filtro de periodo de los listados: "2026", "3T 2026"… o "Todo".</summary>
public static class Filtros
{
    public static List<string> Opciones(IEnumerable<DateOnly> fechas)
    {
        var años = fechas.Select(f => f.Year).Append(DateTime.Today.Year).Distinct().OrderDescending();
        var lista = new List<string>();
        foreach (var a in años)
        {
            lista.Add(a.ToString());
            for (var t = 1; t <= 4; t++) lista.Add($"{t}T {a}");
        }
        lista.Add("Todo");
        return lista;
    }

    public static bool Contiene(string filtro, DateOnly fecha)
    {
        if (filtro == "Todo") return true;
        if (filtro.Length == 4) return fecha.Year.ToString() == filtro;
        return new Trimestre(int.Parse(filtro[3..]), filtro[0] - '0').Contiene(fecha);
    }
}

/// <summary>Editor de una factura: datos, líneas de detalle y totales calculados.</summary>
public static class EditorIngreso
{
    private static readonly decimal[] TiposIva = [0, 4, 10, 21];
    private static readonly decimal[] TiposRetencion = [0, 7, 15];

    public static async Task<bool> Mostrar(Window dueño, Ingreso i, List<Ingreso> todas)
    {
        var clientes = Almacen.Clientes();
        var servicios = Almacen.Servicios();
        var numero = Ui.Texto(i.Numero, "2026-001", 160);
        var fecha = Ui.Fecha(i.Fecha);
        var cliente = Ui.Opciones(clientes, clientes.FirstOrDefault(c => c.Id == (i.Cliente?.Id ?? i.ClienteId)));
        var estado = Ui.Opciones(["Borrador", "Emitida"], i.Borrador ? "Borrador" : "Emitida", 140);
        var concepto = Ui.Texto(i.Concepto, "Descripción general (opcional)");
        var iva = Ui.Opciones(TiposIva.Union([i.TipoIVA]).Order().Select(Formato.Porcentaje).ToList(), Formato.Porcentaje(i.TipoIVA), 110);
        var retencion = Ui.Opciones(TiposRetencion.Union([i.TipoRetencion]).Order().Select(Formato.Porcentaje).ToList(), Formato.Porcentaje(i.TipoRetencion), 110);
        var cobrada = new CheckBox { Content = "Cobrada", IsChecked = i.Cobrada };
        var fechaCobro = Ui.Fecha(i.FechaCobro);
        var notas = Ui.Texto(i.Notas, "Notas internas");
        var resumen = new TextBlock { FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 6, 0, 0) };

        // Líneas: si la factura no tiene (importada sin detalle), se parte de una con su base
        var filas = new List<(TextBox Concepto, TextBox Cantidad, TextBox Precio, TextBlock Importe, string Codigo)>();
        var panelLineas = new StackPanel { Spacing = 6 };
        decimal Tipo(ComboBox c) => decimal.Parse(((string)c.SelectedItem!).Replace(" %", ""), Formato.Es);

        void Actualizar()
        {
            decimal @base = 0;
            foreach (var f in filas)
            {
                var imp = Calculo.Redondear(Ui.Leer(f.Cantidad) * Ui.Leer(f.Precio));
                f.Importe.Text = Formato.Euros(imp);
                @base += imp;
            }
            var cuota = Calculo.Porcentaje(Tipo(iva), @base);
            var ret = Calculo.Porcentaje(Tipo(retencion), @base);
            resumen.Text = $"Base {Formato.Euros(@base)}  ·  IVA {Formato.Euros(cuota)}  ·  Retención {Formato.Euros(-ret)}  ·  Total {Formato.Euros(@base + cuota - ret)}";
        }

        void AñadirLinea(string codigo, string texto, decimal cantidad, decimal precio)
        {
            var c = Ui.Texto(texto, "Concepto");
            var q = Ui.Importe(cantidad, 70);
            q.Text = Formato.Cantidad(cantidad);
            var p = Ui.Importe(precio, 100);
            var imp = new TextBlock { Width = 100, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            var fila = (c, q, p, imp, codigo);
            var quitar = new Button { Content = "✕", Padding = new Thickness(8, 4) };
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto,Auto"), ColumnSpacing = 6 };
            Grid.SetColumn(q, 1); Grid.SetColumn(p, 2); Grid.SetColumn(imp, 3); Grid.SetColumn(quitar, 4);
            g.Children.AddRange([c, q, p, imp, quitar]);
            quitar.Click += (_, _) =>
            {
                if (filas.Count == 1) return;
                filas.Remove(fila);
                panelLineas.Children.Remove(g);
                Actualizar();
            };
            q.TextChanged += (_, _) => Actualizar();
            p.TextChanged += (_, _) => Actualizar();
            filas.Add(fila);
            panelLineas.Children.Add(g);
            Actualizar();
        }

        if (i.Lineas.Count == 0) AñadirLinea("", i.Concepto, 1, i.Base);
        foreach (var l in i.Lineas) AñadirLinea(l.Codigo, l.Concepto, l.Cantidad, l.Precio);
        iva.SelectionChanged += (_, _) => Actualizar();
        retencion.SelectionChanged += (_, _) => Actualizar();

        var servicio = new ComboBox { ItemsSource = servicios, PlaceholderText = "Añadir servicio del catálogo…", MinWidth = 260 };
        servicio.SelectionChanged += (_, _) =>
        {
            if (servicio.SelectedItem is not Servicio s) return;
            if (filas.Count == 1 && filas[0].Concepto.Text is null or "" && Ui.Leer(filas[0].Precio) == 0)
            {
                panelLineas.Children.Clear();
                filas.Clear();
            }
            AñadirLinea(s.Codigo, s.Descripcion, 1, s.Precio);
            servicio.SelectedItem = null;
        };
        fecha.SelectedDateChanged += (_, _) =>
        {
            // Si el número era el sugerido, se vuelve a sugerir para la nueva fecha
            if (i.Id != 0 || Ui.Leer(fecha) is not { } f) return;
            var numeros = todas.OrderBy(x => x.Fecha).Select(x => x.Numero).ToList();
            numero.Text = Ajustes.NumeroSugerido(f, numeros, numeros);
        };

        var cabeceraLineas = new Grid { ColumnDefinitions = new ColumnDefinitions("*,70,100,100,34"), ColumnSpacing = 6 };
        string[] cab = ["Concepto", "Cantidad", "Precio", "Importe"];
        for (var n = 0; n < cab.Length; n++)
        {
            var t = Ui.Nota(cab[n]);
            if (n > 0) t.TextAlignment = TextAlignment.Right;
            Grid.SetColumn(t, n);
            cabeceraLineas.Children.Add(t);
        }

        var contenido = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                Ui.Formulario(("Número", numero), ("Fecha", fecha), ("Cliente", cliente), ("Estado", estado), ("Concepto", concepto)),
                Ui.Subtitulo("Detalle"),
                cabeceraLineas,
                panelLineas,
                Ui.Fila(Ui.Boton("Añadir línea", () => AñadirLinea("", "", 1, 0)), servicio),
                Ui.Formulario(("IVA", iva), ("Retención IRPF", retencion)),
                resumen,
                Ui.Formulario(("Cobro", Ui.Fila(cobrada, fechaCobro)), ("Notas", notas)),
                Ui.Nota("Las facturas en borrador no cuentan para impuestos. Usa «Emitir PDF» para generar la factura definitiva."),
            },
        };
        if (clientes.Count == 0) contenido.Children.Insert(0, Ui.Nota("Aún no hay clientes: créalos en la sección Clientes.", Ui.Aviso));

        string? Validar()
        {
            if (Ui.Leer(fecha) is null) return "Indica la fecha.";
            if (string.IsNullOrWhiteSpace(numero.Text)) return "Indica el número de factura.";
            if (filas.All(f => Ui.Leer(f.Precio) == 0)) return "La factura no tiene importe.";
            var n = numero.Text.Trim();
            if ((string)estado.SelectedItem! == "Emitida" && todas.Any(x => x.Id != i.Id && !x.Borrador && x.Numero == n))
                return $"Ya hay otra factura emitida con el número {n}.";
            return null;
        }

        if (!await Ui.Dialogo(dueño, i.Id == 0 ? "Nueva factura" : $"Factura {i.Numero}", contenido, validar: Validar, ancho: 720))
            return false;

        i.Numero = numero.Text!.Trim();
        i.Fecha = Ui.Leer(fecha)!.Value;
        i.Cliente = cliente.SelectedItem as Cliente;
        i.ClienteId = i.Cliente?.Id;
        i.Borrador = (string)estado.SelectedItem! == "Borrador";
        i.Concepto = concepto.Text?.Trim() ?? "";
        i.TipoIVA = Tipo(iva);
        i.TipoRetencion = Tipo(retencion);
        i.Lineas = filas.Select(f => new LineaIngreso
        {
            Codigo = f.Codigo, Concepto = f.Concepto.Text?.Trim() ?? "", Cantidad = Ui.Leer(f.Cantidad), Precio = Ui.Leer(f.Precio),
        }).ToList();
        if (i.Concepto.Length == 0) i.Concepto = i.Lineas.FirstOrDefault()?.Concepto ?? "";
        i.Recalcular();
        i.Cobrada = !i.Borrador && cobrada.IsChecked == true;
        i.FechaCobro = i.Cobrada ? Ui.Leer(fechaCobro) ?? DateOnly.FromDateTime(DateTime.Today) : null;
        i.Notas = notas.Text?.Trim() ?? "";
        return true;
    }
}
