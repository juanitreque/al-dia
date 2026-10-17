using System.IO.Compression;
using AlDia.Core;
using AlDia.Datos;
using AlDia.Servicios;

namespace AlDia.Tests;

/// <summary>Base de demostración compartida (la base de datos es estática en la app).</summary>
public sealed class BaseDemo
{
    public BaseDemo()
    {
        var carpeta = Path.Combine(Path.GetTempPath(), "AlDiaTests-" + Guid.NewGuid().ToString("N"));
        Almacen.Abrir(carpeta);
        Sistema.CarpetaDocumentos = Path.Combine(carpeta, "Documentos");
        Demo.Rellenar();
    }
}

public class AppTests(BaseDemo _) : IClassFixture<BaseDemo>
{
    [Fact]
    public void GuardaYLeeIngresosConLineas()
    {
        var i = Almacen.Ingresos().First(x => x.Numero == "2026-001");
        Assert.Equal(480, i.Base);
        Assert.Equal(100.80m, i.CuotaIVA);
        Assert.Equal("Estudio Norte, SL", i.Cliente?.Nombre);
        Assert.Single(i.Lineas);
        Assert.Equal(12, i.Lineas[0].Cantidad);
    }

    [Fact]
    public void GeneraLaFacturaEnPdfLegible()
    {
        var i = Almacen.Ingresos().First(x => x.Numero == "2026-002");
        var pdf = FacturaPdf.Generar(i);
        var texto = LecturaPdf.Texto(pdf);
        Assert.Contains("FACTURA", texto);
        Assert.Contains("2026-002", texto);
        Assert.Contains("95,40 €", texto);
        Assert.Contains("ES91 2100 0418 4502 0005 1332", texto);
    }

    [Fact]
    public void ConceptoLargoSeRepartePorLineasSinPerderLetras()
    {
        var i = Almacen.Ingresos().First(x => x.Numero == "2026-002");
        var concepto = "Diseño y maquetación de la nueva identidad corporativa con manual de uso, papelería " +
                       "y plantillas para redes sociales (Ñandú & Cía., «urgente»)";
        var copia = new Ingreso
        {
            Numero = i.Numero, Fecha = i.Fecha, Cliente = i.Cliente, Base = i.Base, CuotaIVA = i.CuotaIVA,
            TipoIVA = i.TipoIVA, Retencion = i.Retencion, TipoRetencion = i.TipoRetencion,
            Lineas = [new LineaIngreso { Concepto = concepto, Cantidad = 1, Precio = i.Base }],
        };
        var texto = LecturaPdf.Texto(FacturaPdf.Generar(copia));
        foreach (var palabra in concepto.Split(' ')) Assert.Contains(palabra, texto);
        Assert.Contains("TOTAL A PAGAR", texto);
    }

    [Fact]
    public void BorradorLlevaMarcaDeAgua()
    {
        // La marca va girada: se comprueban las letras en orden de dibujo, no por renglones.
        static string Letras(byte[] pdf)
        {
            using var documento = UglyToad.PdfPig.PdfDocument.Open(pdf);
            return string.Concat(documento.GetPage(1).Letters.Select(l => l.Value));
        }
        var i = Almacen.Ingresos().First(x => x.Numero == "2026-002");
        Assert.Contains("BORRADOR", Letras(FacturaPdf.Generar(i, borrador: true)));
        Assert.DoesNotContain("BORRADOR", Letras(FacturaPdf.Generar(i)));
    }

    [Fact]
    public void MuchasLineasSiguenEnOtraPaginaYLaFuenteSeRecorta()
    {
        var i = Almacen.Ingresos().First(x => x.Numero == "2026-002");
        var lineas = Enumerable.Range(1, 60)
            .Select(n => new LineaIngreso { Concepto = $"Servicio número {n}", Cantidad = 1, Precio = 10 }).ToList();
        var larga = new Ingreso
        {
            Numero = "2026-099", Fecha = i.Fecha, Cliente = i.Cliente, Base = 600, CuotaIVA = 126, Lineas = lineas,
        };
        var pdf = FacturaPdf.Generar(larga);
        using var documento = UglyToad.PdfPig.PdfDocument.Open(pdf);
        Assert.True(documento.NumberOfPages >= 2);
        var texto = LecturaPdf.Texto(pdf);
        Assert.Contains("Servicio número 1 ", texto);
        Assert.Contains("Servicio número 60 ", texto);
        Assert.Contains("726,00 €", texto);
        // PDFsharp incrusta solo las letras usadas: con la fuente completa pasaría de 600 KB.
        Assert.True(pdf.Length < 150_000, $"El PDF ocupa {pdf.Length} bytes");
    }

    [Fact]
    public void FechasDeVeriFactuYFacturaElectronica()
    {
        Assert.False(VeriFactu.Obliga(new DateOnly(2027, 6, 30)));
        Assert.True(VeriFactu.Obliga(new DateOnly(2027, 7, 1)));
        Assert.False(FacturaElectronica.Obliga(new DateOnly(2028, 10, 5)));
        Assert.True(FacturaElectronica.Obliga(new DateOnly(2028, 10, 6)));
    }

    [Fact]
    public void LeeSuPropioPdfComoGasto()
    {
        var i = Almacen.Ingresos().First(x => x.Numero == "2026-002");
        var leido = LectorTicket.Analizar(LecturaPdf.Texto(FacturaPdf.Generar(i)));
        Assert.Equal(90, leido.Base);
        Assert.Equal(18.90m, leido.CuotaIVA);
        Assert.Equal(new DateOnly(2026, 1, 31), leido.Fecha);
    }

    [Fact]
    public void PaqueteDelGestor()
    {
        var p = new PaqueteGestor(new Trimestre(2026, 3), Almacen.Ingresos(), Almacen.Gastos(), Almacen.Modelos());
        Assert.Equal("Tu Nombre Apellido - 3T 2026", p.Nombre);
        var zip = p.Crear(incluirDocumentos: true);
        Assert.Equal("Tu Nombre Apellido - 3T 2026.zip", Path.GetFileName(zip));
        using var archivo = ZipFile.OpenRead(zip);
        var nombres = archivo.Entries.Select(e => e.FullName.Replace('\\', '/')).ToList();
        Assert.Contains("Tu Nombre Apellido - 3T 2026/Resumen 3T 2026.txt", nombres);
        Assert.Contains("Tu Nombre Apellido - 3T 2026/Ingresos 3T 2026.csv", nombres);
        Assert.Equal(12, nombres.Count(n => n.Contains("/Facturas emitidas/")));
        var resumen = p.Resumen();
        Assert.Contains("[46] Resultado régimen general", resumen);
        Assert.Contains("MODELO 130", resumen);
    }

    [Fact]
    public void BorradorDeCorreoConAdjunto()
    {
        var adjunto = Path.Combine(Path.GetTempPath(), "Prueba Ñ.pdf");
        File.WriteAllBytes(adjunto, [1, 2, 3]);
        var eml = Sistema.BorradorCorreo("gestor@ejemplo.es", "Documentación 3T 2026", "Hola", adjunto, abrir: false);
        var texto = File.ReadAllText(eml);
        Assert.StartsWith("X-Unsent: 1\r\n", texto);
        Assert.Contains("To: gestor@ejemplo.es", texto);
        Assert.Contains("=?utf-8?B?", texto);
        Assert.Contains("AQID", texto);
    }
}
