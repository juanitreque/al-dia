using AlDia.Core;
using AlDia.Datos;
using AlDia.Servicios;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace AlDia.Vistas;

public sealed class FilaModelo(ModeloPresentado m)
{
    public ModeloPresentado Modelo => m;
    public string Nombre => TiposModelo.Todos.FirstOrDefault(t => t.Codigo == m.Modelo).Nombre ?? m.Modelo;
    public string Periodo => $"{m.PeriodoVisible} {m.Ejercicio}";
    public string Resultado => m.TipoResultado;
    public string Importe => Formato.Euros(m.Importe);
    public string Fecha => Formato.Fecha(m.FechaPresentacion);
    public string Justificante => m.Justificante;
    public string Documento => m.TieneDocumento ? "Sí" : "";
}

public sealed class ModelosVista : Seccion
{
    public override string Nombre => "Modelos";

    private readonly ComboBox trimestre = new() { MinWidth = 130 };
    private readonly ContentControl calculo = new();
    private readonly DataGrid tabla = Ui.Tabla(("Modelo", "Nombre", false), ("Periodo", "Periodo", false), ("Resultado", "Resultado", false),
        ("Importe", "Importe", true), ("Presentado", "Fecha", false), ("Justificante", "Justificante", false), ("Doc.", "Documento", false));
    private List<Trimestre> trimestres = [];

    public ModelosVista()
    {
        trimestre.SelectionChanged += (_, _) => Calcular();
        tabla.DoubleTapped += async (_, _) => { if (Seleccionado is { } m) await Editar(m); };
        tabla.Height = 220;
        var contenido = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                Ui.Titulo("Modelos"),
                Ui.Barra(new TextBlock { Text = "Trimestre", VerticalAlignment = VerticalAlignment.Center }, trimestre,
                         Ui.Boton("Paquete para el gestor…", Paquete, principal: true)),
                calculo,
                Ui.Subtitulo("Modelos presentados"),
                Ui.Barra(Ui.Boton("Registrar presentado…", () => Editar(null)),
                         Ui.Boton("Editar", () => Seleccionado is { } m ? Editar(m) : Task.CompletedTask),
                         Ui.Boton("Ver justificante", VerJustificante),
                         Ui.Boton("Eliminar", Eliminar)),
                tabla,
            },
        };
        Content = new ScrollViewer { Content = contenido };
    }

    private ModeloPresentado? Seleccionado => (tabla.SelectedItem as FilaModelo)?.Modelo;
    private Trimestre Elegido => trimestres[Math.Max(0, trimestre.SelectedIndex)];

    public override void Refrescar()
    {
        var hoy = DateOnly.FromDateTime(DateTime.Today);
        var actual = Trimestre.De(hoy);
        var aPresentar = Trimestre.APresentar(hoy);
        trimestres = [];
        for (var t = actual; trimestres.Count < 12; t = t.Anterior) trimestres.Add(t);
        var previo = trimestre.SelectedIndex >= 0 ? trimestres.ElementAtOrDefault(trimestre.SelectedIndex) : aPresentar;
        trimestre.ItemsSource = trimestres.Select(t => t == aPresentar ? $"{t.Etiqueta} (a presentar)" : t.Etiqueta).ToList();
        trimestre.SelectedIndex = Math.Max(0, trimestres.IndexOf(previo));
        tabla.ItemsSource = Almacen.Modelos().Select(m => new FilaModelo(m)).ToList();
        Calcular();
    }

    private void Calcular()
    {
        if (trimestres.Count == 0 || trimestre.SelectedIndex < 0) return;
        var t = Elegido;
        var ingresos = Almacen.Ingresos().Where(i => !i.Borrador).Select(i => i.Datos).ToList();
        var gastos = Almacen.Gastos().Select(g => g.Datos).ToList();
        var modelos = Almacen.Modelos();
        var l303 = new Liquidacion303(t, ingresos, gastos);
        var l130 = new Liquidacion130(t, ingresos, gastos, modelos.Pagos130Anteriores(t));
        var pendiente = modelos.PendienteCompensar303(t);

        var c303 = new List<(string, string, decimal)>();
        foreach (var d in l303.Devengos)
            if (d.Casillas is { } c)
            {
                c303.Add((c.Base, $"Base imponible al {Formato.Porcentaje(d.Tipo)}", d.Base));
                c303.Add((c.Cuota, $"Cuota devengada al {Formato.Porcentaje(d.Tipo)}", d.Cuota));
            }
        c303.Add(("27", "Total cuota devengada", l303.TotalDevengado));
        c303.Add(("28", "Base operaciones interiores corrientes", l303.BaseCorrientes));
        c303.Add(("29", "Cuota deducible operaciones corrientes", l303.CuotaCorrientes));
        if (l303.CuotaInversion > 0)
        {
            c303.Add(("30", "Base bienes de inversión", l303.BaseInversion));
            c303.Add(("31", "Cuota deducible bienes de inversión", l303.CuotaInversion));
        }
        c303.Add(("45", "Total a deducir", l303.TotalDeducir));
        c303.Add(("46", "Resultado régimen general", l303.Resultado));
        if (pendiente > 0)
        {
            c303.Add(("", "Cuotas a compensar de periodos anteriores", -pendiente));
            c303.Add(("", "Resultado de la liquidación", l303.ResultadoCompensando(pendiente)));
        }

        var c130 = new List<(string, string, decimal)>
        {
            ("01", "Ingresos computables (desde el 1 de enero)", l130.Ingresos),
            ("02", "Gastos fiscalmente deducibles", l130.Gastos),
            ("03", "Rendimiento neto", l130.Rendimiento),
            ("04", "20 % del rendimiento", l130.Cuota),
            ("05", "Pagos fraccionados anteriores", -l130.PagosAnteriores),
            ("06", "Retenciones soportadas", -l130.Retenciones),
            ("07", "Pago fraccionado previo", l130.Resultado),
        };
        var pct = Liquidacion130.PorcentajeConRetencion(ingresos, t.Ejercicio - 1) ?? Liquidacion130.PorcentajeConRetencion(ingresos, t.Ejercicio);

        var res303 = pendiente > 0 ? l303.ResultadoCompensando(pendiente) : l303.Resultado;
        var w = new WrapPanel();
        w.Children.Add(Tarjeta("Modelo 303 · IVA", c303, res303 > 0 ? "A ingresar" : res303 < 0 ? (t.Numero == 4 ? "A devolver o compensar" : "A compensar") : "Sin actividad / cero",
                               $"Plazo hasta el {Formato.Fecha(t.PlazoPresentacion)}"));
        w.Children.Add(Tarjeta("Modelo 130 · Pago fraccionado IRPF", c130, l130.Resultado > 0 ? "A ingresar" : "Negativa / cero",
                               pct >= 70 ? $"{Formato.Porcentaje(pct.Value)} de tus ingresos llevan retención: no estás obligado a presentar el 130."
                                         : $"Plazo hasta el {Formato.Fecha(t.PlazoPresentacion)}"));
        var s = new StackPanel { Spacing = 6 };
        s.Children.Add(w);
        s.Children.Add(Ui.Nota("Cifras orientativas calculadas con las facturas emitidas y los gastos registrados. Revísalas antes de presentar en la sede de la AEAT."));
        calculo.Content = s;
    }

    private static Border Tarjeta(string titulo, List<(string Casilla, string Texto, decimal Valor)> filas, string resultado, string nota)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("40,*,Auto"), RowSpacing = 4, ColumnSpacing = 8 };
        for (var n = 0; n < filas.Count; n++)
        {
            g.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var ultima = n == filas.Count - 1;
            var celdas = new Control[]
            {
                Ui.Nota(filas[n].Casilla.Length > 0 ? $"[{filas[n].Casilla}]" : ""),
                new TextBlock { Text = filas[n].Texto, FontWeight = ultima ? FontWeight.SemiBold : FontWeight.Normal, TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = Formato.Euros(filas[n].Valor), FontWeight = ultima ? FontWeight.SemiBold : FontWeight.Normal, HorizontalAlignment = HorizontalAlignment.Right },
            };
            for (var c = 0; c < 3; c++) { Grid.SetRow(celdas[c], n); Grid.SetColumn(celdas[c], c); g.Children.Add(celdas[c]); }
        }
        return new Border
        {
            Width = 430, Margin = new Thickness(0, 0, 14, 14), Padding = new Thickness(16, 12), CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Color.Parse("#E3E3E3")),
            Child = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = titulo, FontWeight = FontWeight.SemiBold, FontSize = 15 },
                    g,
                    new TextBlock { Text = resultado, Foreground = Ui.Acento, FontWeight = FontWeight.SemiBold },
                    Ui.Nota(nota),
                },
            },
        };
    }

    private async Task Editar(ModeloPresentado? existente)
    {
        var t = Elegido;
        var m = existente ?? new ModeloPresentado { Ejercicio = t.Ejercicio, Periodo = t.Periodo };
        var modelo = Ui.Opciones(TiposModelo.Todos.Select(x => x.Nombre).ToList(),
                                 TiposModelo.Todos.FirstOrDefault(x => x.Codigo == m.Modelo).Nombre ?? "Otro", 280);
        var ejercicio = Ui.Texto(m.Ejercicio.ToString(), "2026", 90);
        var periodo = Ui.Opciones(["1T", "2T", "3T", "4T", "Anual"], m.PeriodoVisible, 110);
        var resultado = Ui.Opciones(TiposModelo.Resultados, m.TipoResultado, 180);
        var importe = Ui.Importe(m.Importe);
        var fecha = Ui.Fecha(m.FechaPresentacion);
        var justificante = Ui.Texto(m.Justificante, "NRC del pago o CSV del justificante");
        var notas = Ui.Texto(m.Notas);
        var documento = Ui.Nota(m.TieneDocumento ? $"Justificante: {m.AdjuntoNombre}" : "Sin justificante adjunto");
        var adjuntar = Ui.Boton("Adjuntar justificante PDF…", async () =>
        {
            var archivos = await Ui.ElegirArchivos(Dueño, "Justificante", false, Ui.Documentos);
            if (archivos.FirstOrDefault() is not { } ruta) return;
            m.Adjunto = await File.ReadAllBytesAsync(ruta);
            m.AdjuntoNombre = Path.GetFileName(ruta);
            m.TieneDocumento = true;
            documento.Text = $"Justificante: {m.AdjuntoNombre}";
        });
        // Al elegir 303/130 se propone el importe calculado
        modelo.SelectionChanged += (_, _) =>
        {
            if (existente is not null) return;
            var codigo = TiposModelo.Todos.First(x => x.Nombre == (string)modelo.SelectedItem!).Codigo;
            var ingresos = Almacen.Ingresos().Where(i => !i.Borrador).Select(i => i.Datos).ToList();
            var gastos = Almacen.Gastos().Select(g => g.Datos).ToList();
            decimal? r = codigo switch
            {
                "303" => new Liquidacion303(t, ingresos, gastos).ResultadoCompensando(Almacen.Modelos().PendienteCompensar303(t)),
                "130" => new Liquidacion130(t, ingresos, gastos, Almacen.Modelos().Pagos130Anteriores(t)).Resultado,
                _ => null,
            };
            if (r is not { } v) return;
            importe.Text = AlDia.Core.Importe.Texto(Math.Abs(v));
            resultado.SelectedItem = v > 0 ? "A ingresar" : v < 0 && codigo == "303" ? "A compensar" : "Negativa / cero";
        };
        if (existente is null) modelo.SelectedItem = TiposModelo.Todos[0].Nombre;

        var contenido = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                Ui.Formulario(("Modelo", modelo), ("Ejercicio", ejercicio), ("Periodo", periodo), ("Resultado", resultado),
                              ("Importe", importe), ("Fecha de presentación", fecha), ("Justificante", justificante), ("Notas", notas)),
                Ui.Fila(adjuntar, documento),
            },
        };
        string? Validar() => int.TryParse(ejercicio.Text, out var a) && a is > 2000 and < 2100 ? Ui.Leer(fecha) is null ? "Indica la fecha." : null : "Ejercicio no válido.";
        if (!await Ui.Dialogo(Dueño, existente is null ? "Registrar modelo presentado" : "Modelo presentado", contenido, validar: Validar))
            return;
        m.Modelo = TiposModelo.Todos.First(x => x.Nombre == (string)modelo.SelectedItem!).Codigo;
        m.Ejercicio = int.Parse(ejercicio.Text!);
        m.Periodo = (string)periodo.SelectedItem! == "Anual" ? "0A" : (string)periodo.SelectedItem!;
        m.TipoResultado = (string)resultado.SelectedItem!;
        m.Importe = Math.Abs(Ui.Leer(importe));
        m.FechaPresentacion = Ui.Leer(fecha)!.Value;
        m.Justificante = (justificante.Text ?? "").Trim();
        m.Notas = (notas.Text ?? "").Trim();
        Almacen.Guardar(m, conAdjunto: m.Adjunto is not null);
        Refrescar();
    }

    private async Task VerJustificante()
    {
        if (Seleccionado is not { TieneDocumento: true } m || Almacen.AdjuntoModelo(m.Id) is not { Length: > 0 } datos) return;
        var ruta = Sistema.Temporal(Formato.NombreArchivo(m.AdjuntoNombre ?? "Justificante.pdf"));
        await File.WriteAllBytesAsync(ruta, datos);
        Sistema.Abrir(ruta);
    }

    private async Task Eliminar()
    {
        if (Seleccionado is not { } m) return;
        if (await Ui.Confirmar(Dueño, "Eliminar", $"¿Eliminar el modelo {m.Modelo} del {m.PeriodoVisible} {m.Ejercicio}?"))
        {
            Almacen.Borrar(m);
            Refrescar();
        }
    }

    /// <summary>Zip trimestral para la gestoría: se guarda donde elijas o se prepara un correo con él.</summary>
    private async Task Paquete()
    {
        var p = new PaqueteGestor(Elegido, Almacen.Ingresos(), Almacen.Gastos(), Almacen.Modelos());
        var documentos = new CheckBox { Content = "Incluir los PDF de facturas, tickets y justificantes", IsChecked = true };
        var email = Ui.Texto(Ajustes.Texto(Ajustes.EmailGestor), "gestoria@ejemplo.es");
        var contenido = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = $"Paquete para el gestor · {Elegido.Etiqueta}", FontWeight = FontWeight.SemiBold, FontSize = 15 },
                Ui.Nota($"{p.IngresosTrimestre.Count} factura(s) emitida(s) · {p.GastosTrimestre.Count} gasto(s) · {p.Presentados.Count} modelo(s) presentado(s)", Brushes.Black),
                Ui.Nota("Incluye un resumen con el 303 y el 130, los libros de ingresos y gastos en CSV (se abren con Excel) y, si lo marcas, todos los documentos."),
                documentos,
                Ui.Formulario(("Email del gestor", email)),
            },
        };
        if (p.GastosSinDocumento.Count > 0)
            contenido.Children.Add(Ui.Nota($"{p.GastosSinDocumento.Count} gasto(s) sin documento adjunto: aparecen en el resumen para que el gestor lo sepa.", Ui.Aviso));
        if (p.IngresosTrimestre.Count == 0 && p.GastosTrimestre.Count == 0)
        {
            await Ui.Mensaje(Dueño, "Paquete para el gestor", "No hay facturas emitidas ni gastos en este trimestre.");
            return;
        }

        var accion = "";
        var ventana = new Window
        {
            Title = "Paquete para el gestor", Width = 540, SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, CanResize = false, ShowInTaskbar = false,
        };
        var enviar = Ui.Boton("Enviar por correo…", () => { accion = "enviar"; ventana.Close(); }, principal: true);
        var guardar = Ui.Boton("Guardar ZIP…", () => { accion = "guardar"; ventana.Close(); });
        var cerrar = Ui.Boton("Cerrar", () => ventana.Close());
        cerrar.IsCancel = true;
        var botones = Ui.Fila(cerrar, guardar, enviar);
        botones.HorizontalAlignment = HorizontalAlignment.Right;
        contenido.Children.Add(botones);
        contenido.Margin = new Thickness(20);
        ventana.Content = contenido;
        await ventana.ShowDialog(Dueño);
        Almacen.Ajuste(Ajustes.EmailGestor, email.Text ?? "");
        if (accion.Length == 0) return;

        string zip;
        try { zip = p.Crear(documentos.IsChecked == true); }
        catch (Exception e) { await Ui.Mensaje(Dueño, "Paquete para el gestor", $"No se pudo crear el paquete: {e.Message}"); return; }

        if (accion == "guardar")
        {
            if (await Ui.GuardarComo(Dueño, "Guardar paquete", Path.GetFileName(zip), "zip") is { } destino)
            {
                File.Copy(zip, destino, overwrite: true);
                Sistema.MostrarEnCarpeta(destino);
            }
            return;
        }
        var nombre = Ajustes.Texto(Ajustes.Nombre);
        var asunto = nombre.Length > 0 ? $"Documentación {Elegido.Etiqueta} - {nombre}" : $"Documentación {Elegido.Etiqueta}";
        var cuerpo = $"Hola:\n\nTe adjunto la documentación del {Elegido.Etiqueta}: resumen con los modelos 303 y 130, libros de ingresos y gastos, y las facturas.\n\nUn saludo,\n{nombre}";
        await DialogoCorreo.Enviar(Dueño, email.Text ?? "", asunto, cuerpo, zip);
    }
}
