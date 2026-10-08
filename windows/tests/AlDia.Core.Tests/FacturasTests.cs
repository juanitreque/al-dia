using AlDia.Core;

namespace AlDia.Core.Tests;

public class LectorFacturaTests
{
    /// <summary>Texto con la misma estructura que se extrae de las facturas reales (datos ficticios).</summary>
    private const string Muestra = """
        DATOS CLIENTE:
        Empresa Ejemplo, SL
        C/ Mayor, 1
        FACTURA 28001 Madrid
        Madrid
        Número: F0126 Fecha: 31/01/2026 B12345678
        CONCEPTO UNIDAD PRECIO HONORARIOS
        CONSULTORIA 20 19,00 380,00
        PERSONAL(2) 3 35,00 105,00
        Base I.V.A. % Base I.R.P.F. %
        485,00 21 485,00 15
        Honoraios I.V.A. I.R.P.F.
        IMPORTE LÍQUIDO
        485,00 101,85 -72,75 514,10
        Texto legal con números como 28001 y 21. que no deben confundir al lector.
        """;

    [Fact]
    public void LeeLaPlantilla()
    {
        var f = LectorFactura.Analizar(Muestra);
        Assert.NotNull(f);
        Assert.Equal("F0126", f.Numero);
        Assert.Equal(Ayuda.F(2026, 1, 31), f.Fecha);
        Assert.Equal("Empresa Ejemplo, SL", f.ClienteNombre);
        Assert.Equal("B12345678", f.ClienteNIF);
        Assert.Equal(
            [new LineaLeida("CONSULTORIA", 20, 19, 380), new LineaLeida("PERSONAL(2)", 3, 35, 105)],
            f.Lineas);
        Assert.Equal(485, f.Base);
        Assert.Equal(21, f.TipoIVA);
        Assert.Equal(101.85m, f.CuotaIVA);
        Assert.Equal(15, f.TipoRetencion);
        Assert.Equal(72.75m, f.Retencion);
        Assert.Equal(514.10m, f.Total);
        Assert.Empty(f.Avisos);
    }

    [Fact]
    public void AvisaSiNoCuadra()
    {
        var alterada = Muestra.Replace("PERSONAL(2) 3 35,00 105,00\n", "");
        var f = LectorFactura.Analizar(alterada);
        Assert.NotNull(f);
        Assert.Equal(["Las líneas no suman la base"], f.Avisos);
    }

    [Fact]
    public void IgnoraTextoSinFactura() => Assert.Null(LectorFactura.Analizar("Un PDF cualquiera"));
}

public class NumeracionTests
{
    private static readonly DateOnly Hoy = DateOnly.FromDateTime(DateTime.Today);

    [Fact]
    public void PatronMesAño()
    {
        Assert.Equal("F1026", Numeracion.Siguiente(["F0826", "F0926"], Ayuda.F(2026, 10, 31)));
        Assert.Equal("F1026-2", Numeracion.Siguiente(["F0926", "F1026"], Ayuda.F(2026, 10, 31)));
        Assert.Equal("F0127", Numeracion.Siguiente(["F1226"], Ayuda.F(2027, 1, 31)));
        // Un borrador posterior ya ocupa F1026
        Assert.Equal("F1026-2", Numeracion.Siguiente(["F0926"], Ayuda.F(2026, 10, 3), ["F1026"]));
        Assert.Equal("A003", Numeracion.Siguiente(["A001"], Hoy, ["A002"]));
    }

    [Fact]
    public void SerieAnual()
    {
        Assert.Equal("2027-001", Numeracion.Anual(Ayuda.F(2027, 1, 15), ["F1226", "2026-003"]));
        Assert.Equal("2027-011", Numeracion.Anual(Ayuda.F(2027, 3, 1), ["2027-001", "2027-002", "2027-010"]));
        Assert.Equal("F2027-003", Numeracion.Anual(Ayuda.F(2027, 3, 1), ["2027-005", "F2027-002"], prefijo: "F"));
        Assert.Equal("2027-1000", Numeracion.Anual(Ayuda.F(2027, 3, 1), ["2027-999"]));
    }

    [Fact]
    public void CambioDeAño()
    {
        Assert.Equal("2027-001", Numeracion.Siguiente(["2026-087"], Ayuda.F(2027, 1, 10)));
        Assert.Equal("F2027/01", Numeracion.Siguiente(["F2026/12"], Ayuda.F(2027, 1, 10)));
        Assert.Equal("2026-088", Numeracion.Siguiente(["2026-087"], Ayuda.F(2026, 12, 10)));
    }

    [Fact]
    public void Secuencial()
    {
        Assert.Equal("2026-008", Numeracion.Siguiente(["2026-007"], Ayuda.F(2026, 5, 1)));
        Assert.Equal("A100", Numeracion.Siguiente(["A099"], Hoy));
        Assert.Equal("", Numeracion.Siguiente([], Hoy));
    }
}

public class ExportacionAEATTests
{
    [Fact]
    public void Clientes()
    {
        var texto = ExportacionAEAT.Clientes(
            [new("Empresa #1, SL", "B12345678", CodigoPostal: "28001", Poblacion: "Madrid")]);
        Assert.Equal(ExportacionAEAT.CabeceraClientes + "\r\nEmpresa  1, SL#B12345678##28001#Madrid##ES###\r\n", texto);
    }

    [Fact]
    public void Productos()
    {
        var texto = ExportacionAEAT.Productos([new("WEB F", "Diseño web", 25, 21)]);
        Assert.EndsWith("\r\nWEB_F#Diseño web#25.00###01#01#S1#21.00#\r\n", texto);
    }
}
