using AlDia.Core;

namespace AlDia.Core.Tests;

internal static class Ayuda
{
    public static DateOnly F(int año, int mes, int dia) => new(año, mes, dia);

    public static DatosIngreso Ingreso(DateOnly f, decimal @base, decimal iva = 21, decimal retencion = 15) =>
        new(f, @base, iva, Calculo.Porcentaje(iva, @base), Calculo.Porcentaje(retencion, @base));
}

public class ImporteTests
{
    [Fact]
    public void ParseaFormatosEspañoles()
    {
        Assert.Equal(45.5m, Importe.Parse("45,50"));
        Assert.Equal(1234.56m, Importe.Parse("1.234,56 €"));
        Assert.Equal(45.5m, Importe.Parse("45.5"));
        Assert.Equal(-12.3m, Importe.Parse("-12,3"));
        Assert.Null(Importe.Parse(""));
        Assert.Null(Importe.Parse("12abc"));
    }

    [Fact]
    public void FormateaParaEditar()
    {
        Assert.Equal("1234,50", Importe.Texto(1234.5m));
        Assert.Equal("", Importe.Texto(0));
    }

    [Fact]
    public void RedondeaHalfUp() => Assert.Equal(2.63m, Calculo.Porcentaje(21, 12.5m));
}

public class TrimestreTests
{
    [Fact]
    public void DetectaTrimestre()
    {
        Assert.Equal(new Trimestre(2026, 4), Trimestre.De(Ayuda.F(2026, 10, 3)));
        Assert.Equal(new Trimestre(2026, 1), Trimestre.De(Ayuda.F(2026, 3, 31)));
        Assert.Equal(new Trimestre(2025, 4), Trimestre.APresentar(Ayuda.F(2026, 1, 15)));
    }

    [Fact]
    public void Plazos()
    {
        Assert.Equal(Ayuda.F(2026, 10, 20), new Trimestre(2026, 3).PlazoPresentacion);
        // 30-ene-2027 es sábado → lunes 1 de febrero
        Assert.Equal(Ayuda.F(2027, 2, 1), new Trimestre(2026, 4).PlazoPresentacion);
    }

    [Fact]
    public void PeriodoAnual()
    {
        var p = new Periodo(2026);
        Assert.True(p.Contiene(Ayuda.F(2026, 12, 31)));
        Assert.False(p.Contiene(Ayuda.F(2027, 1, 1)));
    }
}

public class Modelo303Tests
{
    [Fact]
    public void LiquidaTrimestre()
    {
        var t = new Trimestre(2026, 3);
        DatosIngreso[] ingresos =
        [
            Ayuda.Ingreso(Ayuda.F(2026, 7, 31), 600),
            Ayuda.Ingreso(Ayuda.F(2026, 8, 31), 400),
            Ayuda.Ingreso(Ayuda.F(2026, 10, 1), 999), // fuera del trimestre
        ];
        DatosGasto[] gastos =
        [
            new(Ayuda.F(2026, 7, 10), 100, 21),
            new(Ayuda.F(2026, 8, 10), 200, 42, PorcentajeDeducibleIVA: 50), // vehículo
            new(Ayuda.F(2026, 9, 1), 50, 10.5m, FacturaCompleta: false),    // ticket
            new(Ayuda.F(2026, 9, 5), 300, 0),                               // cuota de autónomos, sin IVA
        ];
        var l = new Liquidacion303(t, ingresos, gastos);
        Assert.Single(l.Devengos);
        Assert.Equal(1000, l.Devengos[0].Base);
        Assert.Equal(210, l.TotalDevengado);
        Assert.Equal(200, l.BaseCorrientes);
        Assert.Equal(42, l.CuotaCorrientes);
        Assert.Equal(168, l.Resultado);
        Assert.Equal(-32, l.ResultadoCompensando(200));
    }
}

public class Modelo130Tests
{
    [Fact]
    public void AcumulaDesdeEnero()
    {
        var t = new Trimestre(2026, 2);
        DatosIngreso[] ingresos =
        [
            Ayuda.Ingreso(Ayuda.F(2026, 2, 1), 1000),
            Ayuda.Ingreso(Ayuda.F(2026, 5, 1), 2000),
            Ayuda.Ingreso(Ayuda.F(2025, 12, 1), 5000), // otro ejercicio
        ];
        DatosGasto[] gastos =
        [
            new(Ayuda.F(2026, 3, 1), 400, 84),
            new(Ayuda.F(2026, 4, 1), 100, 21, FacturaCompleta: false), // IVA no deducible → gasto
            new(Ayuda.F(2026, 4, 2), 4000, 840, BienInversion: true),  // se amortiza
        ];
        var l = new Liquidacion130(t, ingresos, gastos, 0);
        Assert.Equal(3000, l.Ingresos);
        Assert.Equal(521, l.Gastos);
        Assert.Equal(2479, l.Rendimiento);
        Assert.Equal(495.8m, l.Cuota);
        Assert.Equal(450, l.Retenciones);
        Assert.Equal(45.8m, l.Resultado);
    }

    [Fact]
    public void ReglaDelSetentaPorCiento()
    {
        DatosIngreso[] ingresos =
        [
            Ayuda.Ingreso(Ayuda.F(2026, 1, 1), 800),
            Ayuda.Ingreso(Ayuda.F(2026, 2, 1), 200, retencion: 0),
        ];
        Assert.Equal(80m, Liquidacion130.PorcentajeConRetencion(ingresos, 2026));
        Assert.Null(Liquidacion130.PorcentajeConRetencion(ingresos, 2025));
    }
}
