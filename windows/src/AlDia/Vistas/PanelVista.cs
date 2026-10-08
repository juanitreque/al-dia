using AlDia.Core;
using AlDia.Datos;
using AlDia.Servicios;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace AlDia.Vistas;

public sealed class PanelVista(Action<Type> irA) : Seccion
{
    public override string Nombre => "Panel";

    public override void Refrescar()
    {
        var hoy = DateOnly.FromDateTime(DateTime.Today);
        var todos = Almacen.Ingresos();
        var ingresos = todos.Where(i => !i.Borrador).ToList();
        var gastos = Almacen.Gastos();
        var modelos = Almacen.Modelos();
        var di = ingresos.Select(i => i.Datos).ToList();
        var dg = gastos.Select(g => g.Datos).ToList();
        var nombre = Ajustes.Texto(Ajustes.Nombre);

        var p = new StackPanel { Spacing = 14 };
        p.Children.Add(new TextBlock { Text = nombre.Length == 0 ? "Al Día" : nombre, FontSize = 28, FontWeight = FontWeight.Bold });
        var fecha = hoy.ToString("dddd, d 'de' MMMM 'de' yyyy", Formato.Es);
        p.Children.Add(Ui.Nota(char.ToUpper(fecha[0]) + fecha[1..]));

        if (nombre.Length == 0)
            p.Children.Add(Tarjeta("#FFF4E5", Ui.Nota("Empieza por Ajustes: tu nombre, NIF y domicilio salen en las facturas y en el paquete para el gestor.", Brushes.Black),
                                   Ui.Boton("Ir a Ajustes", () => irA(typeof(AjustesVista)))));

        // Próximo plazo
        var t = Trimestre.APresentar(hoy);
        var plazo = t.PlazoPresentacion;
        var dias = plazo.DayNumber - hoy.DayNumber;
        var p303 = modelos.FirstOrDefault(m => m.Corresponde("303", t));
        var p130 = modelos.FirstOrDefault(m => m.Corresponde("130", t));
        var pct = Liquidacion130.PorcentajeConRetencion(di, t.Ejercicio - 1) ?? Liquidacion130.PorcentajeConRetencion(di, t.Ejercicio);
        var exento130 = (pct ?? 0) >= 70;
        var plazoTexto = new StackPanel { Spacing = 4 };
        plazoTexto.Children.Add(new TextBlock
        {
            Text = $"Modelos del {t.Etiqueta}: plazo hasta el {Formato.Fecha(plazo)}" +
                   (dias >= 0 ? $" (quedan {dias} días)" : " (plazo vencido)"),
            FontWeight = FontWeight.SemiBold, FontSize = 15, TextWrapping = TextWrapping.Wrap,
        });
        plazoTexto.Children.Add(Ui.Nota("303 (IVA): " + (p303 is null ? "pendiente" : $"presentado el {Formato.Fecha(p303.FechaPresentacion)}")));
        plazoTexto.Children.Add(Ui.Nota("130 (IRPF): " + (p130 is not null ? $"presentado el {Formato.Fecha(p130.FechaPresentacion)}"
            : exento130 ? $"no obligatorio ({Formato.Porcentaje(pct!.Value)} de ingresos con retención)" : "pendiente")));
        p.Children.Add(Tarjeta(p303 is null && dias <= 7 ? "#FFF4E5" : "#EAF3FB", plazoTexto,
                               Ui.Boton("Ver modelos", () => irA(typeof(ModelosVista)))));

        var borradores = todos.Where(i => i.Borrador).ToList();
        if (borradores.Count > 0)
            p.Children.Add(Tarjeta("#FFF4E5",
                Ui.Nota($"{borradores.Count} factura(s) en borrador pendientes de emitir: {string.Join(", ", borradores.Select(b => b.Numero))}", Brushes.Black),
                Ui.Boton("Ir a ingresos", () => irA(typeof(IngresosVista)))));

        var enCurso = Trimestre.De(hoy);
        var l303 = new Liquidacion303(enCurso, di, dg);
        p.Children.Add(Bloque($"Trimestre en curso · {enCurso.Etiqueta}",
            ("Facturado (base)", Formato.Euros(ingresos.Where(i => enCurso.Contiene(i.Fecha)).Sum(i => i.Base)), ""),
            ("IVA repercutido", Formato.Euros(l303.TotalDevengado), ""),
            ("IVA deducible", Formato.Euros(l303.TotalDeducir), ""),
            ("303 estimado", Formato.Euros(l303.Resultado), l303.Resultado >= 0 ? "a ingresar" : "a compensar")));

        var año = hoy.Year;
        var ia = ingresos.Where(i => i.Fecha.Year == año).ToList();
        var ga = dg.Where(g => g.Fecha.Year == año).ToList();
        p.Children.Add(Bloque($"Ejercicio {año}",
            ("Ingresos (base)", Formato.Euros(ia.Sum(i => i.Base)), ""),
            ("Gastos deducibles IRPF", Formato.Euros(ga.Sum(g => g.GastoIRPF)), ""),
            ("Rendimiento neto", Formato.Euros(ia.Sum(i => i.Base) - ga.Sum(g => g.GastoIRPF)), ""),
            ("Retenciones soportadas", Formato.Euros(ia.Sum(i => i.Retencion)), "a descontar en la renta")));

        var pendientes = ingresos.Where(i => !i.Cobrada).ToList();
        p.Children.Add(Bloque("Cobros",
            ("Pendiente de cobro", Formato.Euros(pendientes.Sum(i => i.Total)),
             pendientes.Count == 0 ? "todo cobrado" : $"{pendientes.Count} factura(s)")));

        Content = new ScrollViewer { Content = p };
    }

    private static Border Tarjeta(string fondo, Control texto, Control? accion = null)
    {
        var d = new DockPanel();
        if (accion is not null)
        {
            DockPanel.SetDock(accion, Dock.Right);
            accion.VerticalAlignment = VerticalAlignment.Center;
            accion.Margin = new Thickness(12, 0, 0, 0);
            d.Children.Add(accion);
        }
        d.Children.Add(texto);
        return new Border { Background = new SolidColorBrush(Color.Parse(fondo)), CornerRadius = new CornerRadius(8), Padding = new Thickness(16, 12), Child = d };
    }

    private static Control Bloque(string titulo, params (string Titulo, string Valor, string Nota)[] fichas)
    {
        var s = new StackPanel { Spacing = 6 };
        s.Children.Add(Ui.Subtitulo(titulo));
        var w = new WrapPanel();
        foreach (var (t, v, n) in fichas)
        {
            var f = new StackPanel { Spacing = 2 };
            f.Children.Add(Ui.Nota(t));
            f.Children.Add(new TextBlock { Text = v, FontSize = 20, FontWeight = FontWeight.SemiBold });
            if (n.Length > 0) f.Children.Add(Ui.Nota(n));
            w.Children.Add(new Border
            {
                Child = f, Width = 200, Padding = new Thickness(14, 10), Margin = new Thickness(0, 0, 10, 10),
                CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.Parse("#E3E3E3")),
            });
        }
        s.Children.Add(w);
        return s;
    }
}
