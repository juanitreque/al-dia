using AlDia.Core;
using AlDia.Datos;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;

namespace AlDia.Servicios;

/// <summary>Factura en PDF (A4, texto seleccionable) con los datos obligatorios del art. 6 del Reglamento de facturación.
/// Se dibuja con PDFsharp (MIT) sobre una página A4 fija con las mismas medidas que la plantilla de la versión para Mac.</summary>
public static class FacturaPdf
{
    static FacturaPdf() => FuentesFactura.Registrar();

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

        using var documento = new PdfDocument();
        documento.Info.Title = $"Factura {i.Numero}";
        documento.Info.Author = e.Nombre;
        documento.Info.Creator = "Al Día";
        new Pagina(documento, e.PieFinal, borrador).Dibujar(i, e, lineas);

        using var flujo = new MemoryStream();
        documento.Save(flujo, false);
        return flujo.ToArray();
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

    /// <summary>Dibujo de la factura: medidas en puntos, origen arriba a la izquierda.</summary>
    private sealed class Pagina(PdfDocument documento, string pie, bool borrador)
    {
        private const double Ancho = 595, Alto = 842, Margen = 48;
        private const double Contenido = Ancho - 2 * Margen;
        private const double Separacion = 22;
        private const double ColCantidad = 60, ColPrecio = 80, ColImporte = 90, Hueco = 8;
        private const double AnchoTotales = 240;
        private const double TamPie = 7.5, InterlineaPie = 1.25;

        private static readonly XColor Negro = XColors.Black;
        private static readonly XColor Gris = XColor.FromArgb(0x66, 0x66, 0x66);
        private static readonly XColor Linea = XColor.FromArgb(0xD9, 0xD9, 0xD9);
        private static readonly XColor Marca = XColor.FromArgb(0x1F, 0xFF, 0x00, 0x00);

        private XGraphics lienzo = null!;
        private double y;
        private double limite;

        private static XFont Fuente(double tamaño, bool negrita = false) =>
            new(FuentesFactura.Familia, tamaño, negrita ? XFontStyleEx.Bold : XFontStyleEx.Regular);

        private static XPen Trazo(XColor color, double grosor) => new(color, grosor);

        private static double AltoLinea(XFont f) => f.Size * 1.2;

        public void Dibujar(Ingreso i, Emisor e, List<(string Concepto, decimal Cantidad, decimal Precio, decimal Importe)> lineas)
        {
            NuevaPagina();
            Cabecera(i, e);
            Cliente(i);
            Tabla(lineas);
            Totales(i);
            if (e.Iban.Length > 0) FormaDePago(e);
            lienzo.Dispose();
        }

        // MARK: Páginas

        private void NuevaPagina()
        {
            var pagina = documento.AddPage();
            pagina.Width = XUnit.FromPoint(Ancho);
            pagina.Height = XUnit.FromPoint(Alto);
            lienzo = XGraphics.FromPdfPage(pagina);
            y = Margen;
            var altoPie = AltoPie();
            limite = Alto - Margen - (altoPie > 0 ? altoPie + Separacion : 0);
            DibujarPie();
            if (borrador) DibujarMarcaDeAgua();
        }

        /// <summary>Si no cabe un bloque de la altura indicada, sigue en otra página.</summary>
        private bool Asegurar(double alto)
        {
            if (y + alto <= limite) return false;
            lienzo.Dispose();
            NuevaPagina();
            return true;
        }

        private void DibujarMarcaDeAgua()
        {
            var f = Fuente(110, negrita: true);
            var estado = lienzo.Save();
            lienzo.RotateAtTransform(-35, new XPoint(Ancho / 2, Alto / 2));
            lienzo.DrawString("BORRADOR", f, new XSolidBrush(Marca), new XPoint(Ancho / 2, Alto / 2), XStringFormats.Center);
            lienzo.Restore(estado);
        }

        // MARK: Bloques

        private void Cabecera(Ingreso i, Emisor e)
        {
            var titulo = Fuente(26, negrita: true);
            var numero = Fuente(13, negrita: true);
            var normal = Fuente(10.5);

            // Columna derecha: FACTURA, número y fecha, alineados a la derecha.
            var derecha = Ancho - Margen;
            var anchoTitulo = AnchoEspaciado("FACTURA", titulo, 1.3);
            var anchoDerecha = Math.Max(anchoTitulo,
                Math.Max(Medir($"Nº {i.Numero}", numero), Medir($"Fecha: {Formato.Fecha(i.Fecha)}", normal)));
            var yd = TextoEspaciado("FACTURA", titulo, 1.3, derecha - anchoTitulo, y, Negro) + 2;
            yd = TextoDerecha($"Nº {i.Numero}", numero, derecha, yd) + 2;
            yd = TextoDerecha($"Fecha: {Formato.Fecha(i.Fecha)}", normal, derecha, yd);

            // Columna izquierda: datos del emisor, sin invadir la derecha.
            var anchoIzquierda = Contenido - anchoDerecha - 16;
            var yi = Parrafo(e.Nombre, Fuente(17, negrita: true), Margen, y, anchoIzquierda) + 2;
            yi = Parrafo($"NIF {e.Nif}", normal, Margen, yi, anchoIzquierda) + 2;
            yi = Parrafo(e.Direccion, normal, Margen, yi, anchoIzquierda) + 2;
            yi = Parrafo(e.Localidad, normal, Margen, yi, anchoIzquierda);
            var contacto = string.Join(" · ", new[] { e.Email, e.Telefono }.Where(s => s.Length > 0));
            if (contacto.Length > 0) yi = Parrafo(contacto, normal, Margen, yi + 2, anchoIzquierda, Gris);

            y = Math.Max(yi, yd) + Separacion;
        }

        private void Cliente(Ingreso i)
        {
            const double relleno = 12;
            var etiqueta = Fuente(9, negrita: true);
            var nombre = Fuente(12, negrita: true);
            var normal = Fuente(10.5);
            var ancho = Contenido - 2 * relleno;

            var filas = new List<(string Texto, XFont Fuente)>();
            if (i.Cliente is { } c)
            {
                filas.Add((c.Nombre, nombre));
                filas.Add(($"NIF {c.Nif}", normal));
                filas.Add((c.Direccion, normal));
                filas.Add((Formato.Localidad(c.CodigoPostal, c.Poblacion, c.Provincia), normal));
            }
            var alto = 2 * relleno + AltoLinea(etiqueta)
                + filas.Sum(f => 2 + Envolver(f.Texto, f.Fuente, ancho).Count * AltoLinea(f.Fuente));
            Asegurar(alto);

            lienzo.DrawRoundedRectangle(Trazo(Linea, 0.8), Margen, y, Contenido, alto, 12, 12);
            var yc = TextoEspaciado("CLIENTE", etiqueta, 0.72, Margen + relleno, y + relleno, Gris);
            foreach (var (texto, fuente) in filas) yc = Parrafo(texto, fuente, Margen + relleno, yc + 2, ancho);
            y += alto + Separacion;
        }

        private void Tabla(List<(string Concepto, decimal Cantidad, decimal Precio, decimal Importe)> lineas)
        {
            var cabecera = Fuente(9, negrita: true);
            var normal = Fuente(10.5);
            var anchoConcepto = Contenido - ColCantidad - ColPrecio - ColImporte - 3 * Hueco;
            var xCantidad = Ancho - Margen - ColImporte - ColPrecio - 2 * Hueco;
            var xPrecio = Ancho - Margen - ColImporte - Hueco;
            var xImporte = Ancho - Margen;

            void Cabecera()
            {
                var alto = 12 + AltoLinea(cabecera);
                Asegurar(alto + 12 + AltoLinea(normal));
                Texto("Concepto", cabecera, Margen, y + 6, Gris);
                TextoDerecha("Cantidad", cabecera, xCantidad, y + 6, Gris);
                TextoDerecha("Precio", cabecera, xPrecio, y + 6, Gris);
                TextoDerecha("Importe", cabecera, xImporte, y + 6, Gris);
                y += alto;
                lienzo.DrawLine(Trazo(Negro, 0.8), Margen, y, Ancho - Margen, y);
            }

            Cabecera();
            var separador = Trazo(Linea, 0.5);
            foreach (var (concepto, cantidad, precio, importe) in lineas)
            {
                var renglones = Envolver(concepto, normal, anchoConcepto);
                var alto = 12 + renglones.Count * AltoLinea(normal);
                if (Asegurar(alto)) Cabecera();
                var yf = y + 6;
                foreach (var r in renglones) yf = Texto(r, normal, Margen, yf);
                TextoDerecha(Formato.Cantidad(cantidad), normal, xCantidad, y + 6);
                TextoDerecha(Formato.Euros(precio), normal, xPrecio, y + 6);
                TextoDerecha(Formato.Euros(importe), normal, xImporte, y + 6);
                y += alto;
                lienzo.DrawLine(separador, Margen, y, Ancho - Margen, y);
            }
            y += Separacion;
        }

        private void Totales(Ingreso i)
        {
            var normal = Fuente(10.5);
            var destacado = Fuente(13, negrita: true);
            var izquierda = Ancho - Margen - AnchoTotales;
            var derecha = Ancho - Margen;

            var filas = new List<(string Titulo, decimal Valor)>
            {
                ("Base imponible", i.Base),
                ($"IVA {Formato.Porcentaje(i.TipoIVA)}", i.CuotaIVA),
            };
            if (i.Retencion != 0) filas.Add(($"Retención IRPF {Formato.Porcentaje(i.TipoRetencion)}", -i.Retencion));
            Asegurar(filas.Count * (AltoLinea(normal) + 5) + 6 + AltoLinea(destacado));

            foreach (var (titulo, valor) in filas)
            {
                Texto(titulo, normal, izquierda, y);
                y = TextoDerecha(Formato.Euros(valor), normal, derecha, y) + 5;
            }
            lienzo.DrawLine(Trazo(Negro, 0.8), izquierda, y, derecha, y);
            y += 6;
            Texto("TOTAL A PAGAR", destacado, izquierda, y);
            y = TextoDerecha(Formato.Euros(i.Total), destacado, derecha, y) + Separacion;
        }

        private void FormaDePago(Emisor e)
        {
            var etiqueta = Fuente(9, negrita: true);
            var normal = Fuente(10.5);
            var texto = $"Transferencia bancaria a {IBAN.Formatear(e.Iban)}";
            Asegurar(AltoLinea(etiqueta) + 2 + Envolver(texto, normal, Contenido).Count * AltoLinea(normal));
            y = TextoEspaciado("FORMA DE PAGO", etiqueta, 0.72, Margen, y, Gris) + 2;
            y = Parrafo(texto, normal, Margen, y, Contenido);
        }

        // MARK: Pie justificado

        private double AltoPie() =>
            pie.Length == 0 ? 0 : Envolver(pie, Fuente(TamPie), Contenido).Count * TamPie * InterlineaPie;

        /// <summary>Pie pegado al margen inferior, justificado (la última línea de cada párrafo, a la izquierda).</summary>
        private void DibujarPie()
        {
            if (pie.Length == 0) return;
            var f = Fuente(TamPie);
            var pincel = new XSolidBrush(Gris);
            var interlinea = TamPie * InterlineaPie;
            var yp = Alto - Margen - AltoPie();
            foreach (var parrafo in pie.Replace("\r", "").Split('\n'))
            {
                var renglones = Envolver(parrafo, f, Contenido);
                for (var n = 0; n < renglones.Count; n++)
                {
                    var palabras = renglones[n].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    var linea = LineaBase(f, yp, interlinea);
                    if (n == renglones.Count - 1 || palabras.Length < 2)
                        lienzo.DrawString(renglones[n], f, pincel, Margen, linea, XStringFormats.BaseLineLeft);
                    else
                    {
                        var hueco = (Contenido - palabras.Sum(w => Medir(w, f))) / (palabras.Length - 1);
                        var x = Margen;
                        foreach (var w in palabras)
                        {
                            lienzo.DrawString(w, f, pincel, x, linea, XStringFormats.BaseLineLeft);
                            x += Medir(w, f) + hueco;
                        }
                    }
                    yp += interlinea;
                }
            }
        }

        // MARK: Texto

        /// <summary>Línea base de un renglón de la altura indicada, con el texto centrado verticalmente.</summary>
        private static double LineaBase(XFont f, double arriba, double alto)
        {
            var m = f.Metrics;
            var ascenso = m.Ascent * f.Size / m.UnitsPerEm;
            var descenso = Math.Abs(m.Descent) * f.Size / m.UnitsPerEm;
            return arriba + (alto - ascenso - descenso) / 2 + ascenso;
        }

        private double Medir(string texto, XFont f) => lienzo.MeasureString(texto, f).Width;

        /// <summary>Dibuja una línea a partir de su parte superior y devuelve dónde empieza la siguiente.</summary>
        private double Texto(string texto, XFont f, double x, double arriba, XColor? color = null)
        {
            lienzo.DrawString(texto, f, new XSolidBrush(color ?? Negro), x, LineaBase(f, arriba, AltoLinea(f)), XStringFormats.BaseLineLeft);
            return arriba + AltoLinea(f);
        }

        private double TextoDerecha(string texto, XFont f, double derecha, double arriba, XColor? color = null) =>
            Texto(texto, f, derecha - Medir(texto, f), arriba, color);

        private double Parrafo(string texto, XFont f, double x, double arriba, double ancho, XColor? color = null)
        {
            foreach (var r in Envolver(texto, f, ancho)) arriba = Texto(r, f, x, arriba, color);
            return arriba;
        }

        private double AnchoEspaciado(string texto, XFont f, double espacio) =>
            Medir(texto, f) + espacio * (texto.Length - 1);

        /// <summary>Texto con espaciado entre letras (rótulos en mayúsculas).</summary>
        private double TextoEspaciado(string texto, XFont f, double espacio, double x, double arriba, XColor color)
        {
            var linea = LineaBase(f, arriba, AltoLinea(f));
            var pincel = new XSolidBrush(color);
            foreach (var letra in texto)
            {
                var s = letra.ToString();
                lienzo.DrawString(s, f, pincel, x, linea, XStringFormats.BaseLineLeft);
                x += Medir(s, f) + espacio;
            }
            return arriba + AltoLinea(f);
        }

        /// <summary>Reparte el texto en renglones que caben en el ancho; corta palabras solo si no caben enteras.</summary>
        private List<string> Envolver(string texto, XFont f, double ancho)
        {
            var renglones = new List<string>();
            foreach (var parrafo in texto.Replace("\r", "").Split('\n'))
            {
                var actual = "";
                foreach (var palabra in parrafo.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    var prueba = actual.Length == 0 ? palabra : actual + " " + palabra;
                    if (Medir(prueba, f) <= ancho) { actual = prueba; continue; }
                    if (actual.Length > 0) renglones.Add(actual);
                    actual = palabra;
                    while (Medir(actual, f) > ancho && actual.Length > 1)
                    {
                        var corte = actual.Length - 1;
                        while (corte > 1 && Medir(actual[..corte], f) > ancho) corte--;
                        renglones.Add(actual[..corte]);
                        actual = actual[corte..];
                    }
                }
                renglones.Add(actual);
            }
            return renglones;
        }
    }
}

/// <summary>Lato (SIL Open Font License 1.1) incrustada en la app, para que la factura sea igual en cualquier equipo.
/// PDFsharp solo incrusta en el PDF las letras que se usan.</summary>
internal sealed class FuentesFactura : IFontResolver
{
    public const string Familia = "Lato";
    private static readonly object Cerrojo = new();

    public static void Registrar()
    {
        lock (Cerrojo)
            if (GlobalFontSettings.FontResolver is not FuentesFactura) GlobalFontSettings.FontResolver = new FuentesFactura();
    }

    public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic) =>
        new(bold ? "Lato-Bold" : "Lato-Regular");

    public byte[]? GetFont(string faceName)
    {
        using var recurso = typeof(FuentesFactura).Assembly.GetManifestResourceStream($"AlDia.Fuentes.{faceName}.ttf")
            ?? throw new InvalidOperationException($"Falta la fuente {faceName} en los recursos de la app.");
        using var copia = new MemoryStream();
        recurso.CopyTo(copia);
        return copia.ToArray();
    }
}
