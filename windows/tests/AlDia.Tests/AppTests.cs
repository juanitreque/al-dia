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
