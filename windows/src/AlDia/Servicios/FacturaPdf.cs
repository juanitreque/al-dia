using AlDia.Core;
using AlDia.Datos;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace AlDia.Servicios;

/// <summary>Factura en PDF (A4, texto seleccionable) con los datos obligatorios del art. 6 del Reglamento de facturación.</summary>
public static class FacturaPdf
{
    private const string Gris = "#666666";
    private const string Linea = "#D9D9D9";

    static FacturaPdf() => QuestPDF.Settings.License = LicenseType.Community;

    /// <summary>Lo que falta para que la factura tenga los datos obligatorios.</summary>
    public static List<string> DatosQueFaltan(Ingreso i, Emisor e)
    {
        var faltan = new List<string>();
        if (e.Nombre.Length == 0) faltan.Add("Tu nombre (Ajustes)");
        if (e.Nif.Length == 0) faltan.Add("Tu NIF (Ajustes)");
        if (e.Direccion.Length == 0 || e.Poblacion.Length == 0) faltan.Add("Tu domicilio fiscal (Ajustes)");
        if (i.Cliente is { } c)
        {
            if (c.Nif.Length == 0) faltan.Add("NIF del cliente (Clientes)");
            if (c.Direccion.Length == 0 || c.Poblacion.Length == 0) faltan.Add("Domicilio del cliente (Clientes)");
        }
        else faltan.Add("Cliente de la factura");
        if (i.Numero.Trim().Length == 0) faltan.Add("Número de factura");
        return faltan;
    }

    public static byte[] Generar(Ingreso i, Emisor? emisor = null, bool borrador = false)
    {
        var e = emisor ?? new Emisor();
        var lineas = i.Lineas.Count > 0
            ? i.Lineas.Select(l => (l.Concepto, l.Cantidad, l.Precio, l.Importe)).ToList()
            : [(i.Concepto, 1m, i.Base, i.Base)];

        return Document.Create(doc => doc.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(48, Unit.Point);
            page.PageColor(Colors.White);
            page.DefaultTextStyle(t => t.FontSize(10.5f).FontColor(Colors.Black));

            page.Content().Column(col =>
            {
                col.Spacing(22);

                col.Item().Row(row =>
                {
                    row.RelativeItem().Column(c =>
                    {
                        c.Spacing(2);
                        c.Item().Text(e.Nombre).FontSize(17).SemiBold();
                        c.Item().Text($"NIF {e.Nif}");
                        c.Item().Text(e.Direccion);
                        c.Item().Text(e.Localidad);
                        var contacto = string.Join(" · ", new[] { e.Email, e.Telefono }.Where(s => s.Length > 0));
                        if (contacto.Length > 0) c.Item().Text(contacto).FontColor(Gris);
                    });
                    row.AutoItem().AlignRight().Column(c =>
                    {
                        c.Spacing(2);
                        c.Item().AlignRight().Text("FACTURA").FontSize(26).Bold().LetterSpacing(0.05f);
                        c.Item().AlignRight().Text($"Nº {i.Numero}").FontSize(13).SemiBold();
                        c.Item().AlignRight().Text($"Fecha: {Formato.Fecha(i.Fecha)}");
                    });
                });

                col.Item().Border(0.8f).BorderColor(Linea).Padding(12).Column(c =>
                {
                    c.Spacing(2);
                    c.Item().Text("CLIENTE").FontSize(9).SemiBold().FontColor(Gris).LetterSpacing(0.08f);
                    if (i.Cliente is { } cl)
                    {
                        c.Item().Text(cl.Nombre).FontSize(12).SemiBold();
                        c.Item().Text($"NIF {cl.Nif}");
                        c.Item().Text(cl.Direccion);
                        c.Item().Text(Formato.Localidad(cl.CodigoPostal, cl.Poblacion, cl.Provincia));
                    }
                });

                col.Item().Table(t =>
                {
                    t.ColumnsDefinition(c =>
                    {
                        c.RelativeColumn();
                        c.ConstantColumn(60);
                        c.ConstantColumn(80);
                        c.ConstantColumn(90);
                    });
                    t.Header(h =>
                    {
                        static IContainer Cab(IContainer x) => x.BorderBottom(0.8f).BorderColor(Colors.Black).PaddingVertical(6);
                        h.Cell().Element(Cab).Text("Concepto").FontSize(9).SemiBold().FontColor(Gris);
                        h.Cell().Element(Cab).AlignRight().Text("Cantidad").FontSize(9).SemiBold().FontColor(Gris);
                        h.Cell().Element(Cab).AlignRight().Text("Precio").FontSize(9).SemiBold().FontColor(Gris);
                        h.Cell().Element(Cab).AlignRight().Text("Importe").FontSize(9).SemiBold().FontColor(Gris);
                    });
                    foreach (var l in lineas)
                    {
                        static IContainer Celda(IContainer x) => x.BorderBottom(0.5f).BorderColor(Linea).PaddingVertical(6);
                        t.Cell().Element(Celda).Text(l.Item1);
                        t.Cell().Element(Celda).AlignRight().Text(Formato.Cantidad(l.Item2));
                        t.Cell().Element(Celda).AlignRight().Text(Formato.Euros(l.Item3));
                        t.Cell().Element(Celda).AlignRight().Text(Formato.Euros(l.Item4));
                    }
                });

                col.Item().AlignRight().Width(240).Column(c =>
                {
                    c.Spacing(5);
                    void Total(string titulo, decimal valor, bool destacado = false) => c.Item().Row(r =>
                    {
                        var izq = r.RelativeItem().Text(titulo);
                        var der = r.AutoItem().Text(Formato.Euros(valor));
                        if (destacado) { izq.FontSize(13).Bold(); der.FontSize(13).Bold(); }
                    });
                    Total("Base imponible", i.Base);
                    Total($"IVA {Formato.Porcentaje(i.TipoIVA)}", i.CuotaIVA);
                    if (i.Retencion != 0) Total($"Retención IRPF {Formato.Porcentaje(i.TipoRetencion)}", -i.Retencion);
                    c.Item().LineHorizontal(0.8f).LineColor(Colors.Black);
                    Total("TOTAL A PAGAR", i.Total, destacado: true);
                });

                if (e.Iban.Length > 0)
                    col.Item().Column(c =>
                    {
                        c.Spacing(2);
                        c.Item().Text("FORMA DE PAGO").FontSize(9).SemiBold().FontColor(Gris).LetterSpacing(0.08f);
                        c.Item().Text($"Transferencia bancaria a {IBAN.Formatear(e.Iban)}");
                    });
            });

            var pie = e.PieFinal;
            if (pie.Length > 0)
                page.Footer().Text(t =>
                {
                    t.Justify();
                    t.Span(pie).FontSize(7.5f).FontColor(Gris);
                });

            if (borrador)
                page.Foreground().AlignCenter().AlignMiddle().Rotate(-35)
                    .Text("BORRADOR").FontSize(110).ExtraBold().FontColor("#1FFF0000");
        })).GeneratePdf();
    }

    public static string NombreArchivo(Ingreso i) => Formato.NombreArchivo($"Factura {i.Numero.Replace('/', '-')}.pdf");

    /// <summary>Copia en Documentos\Al Día\Facturas\&lt;año&gt;\ (se sobrescribe si ya existía).</summary>
    public static string GuardarCopia(byte[] datos, Ingreso i)
    {
        var carpeta = Path.Combine(Sistema.CarpetaDocumentos, "Facturas", i.Fecha.Year.ToString());
        Directory.CreateDirectory(carpeta);
        var ruta = Path.Combine(carpeta, NombreArchivo(i));
        File.WriteAllBytes(ruta, datos);
        return ruta;
    }
}
