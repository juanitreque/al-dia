using AlDia.Core;

namespace AlDia.Core.Tests;

public class LectorTicketTests
{
    [Fact]
    public void FacturaCompleta()
    {
        const string texto = """
            TIENDA EJEMPLO SL
            CIF: B-87654321
            C/ Mayor 5, Madrid
            FACTURA Nº: A-2026/1234
            Fecha: 12/09/2026
            Cliente: Tu Nombre  NIF 00000000T
            Teclado 1 82,64
            Base imponible 82,64
            IVA 21% 17,35
            TOTAL 99,99 €
            """;
        var g = LectorTicket.Analizar(texto, miNIF: "00000000-T");
        Assert.Equal("TIENDA EJEMPLO SL", g.Proveedor);
        Assert.Equal("B87654321", g.NifProveedor);
        Assert.Equal("A-2026/1234", g.NumeroFactura);
        Assert.Equal(Ayuda.F(2026, 9, 12), g.Fecha);
        Assert.Equal(21, g.TipoIVA);
        Assert.Equal(82.64m, g.Base);
        Assert.Equal(17.35m, g.CuotaIVA);
        Assert.True(g.AMiNombre);
    }

    [Fact]
    public void TicketSoloConTotal()
    {
        const string texto = """
            SUPERMERCADO EJEMPLO
            NIF A12345674
            FACTURA SIMPLIFICADA 0042-000123
            12-03-26 18:45
            BANDAS ELASTICAS 12,10
            TOTAL 12,10
            IVA INCLUIDO 21%
            """;
        var g = LectorTicket.Analizar(texto, miNIF: "00000000T");
        Assert.Equal("SUPERMERCADO EJEMPLO", g.Proveedor);
        Assert.Equal("A12345674", g.NifProveedor);
        Assert.Equal("0042-000123", g.NumeroFactura);
        Assert.Equal(Ayuda.F(2026, 3, 12), g.Fecha);
        Assert.Equal(10m, g.Base);
        Assert.Equal(2.1m, g.CuotaIVA);
        Assert.False(g.AMiNombre);
    }

    /// <summary>Factura de un servicio en la nube: etiquetas e importes en columnas separadas,
    /// fecha con el mes en letra, el cliente arriba y el proveedor (irlandés) abajo.</summary>
    [Fact]
    public void FacturaEnColumnasDeProveedorUE()
    {
        const string texto = """
            Factura
            Número de factura: 1234567890-1
            Facturar a
            Tu Nombre Apellido
            C/ Mayor, 1
            28001 Madrid
            Spain
            Número de IVA: ES 00000000T
            Detalles
            Fecha de la factura
            ..............................................................
            16 sept 2026
            Total en EUR
            Subtotal en EUR
            I.V.A. (21%)
            Total en EUR
            Capital social: 1.000.000 EUR
            Proveedor Nube Limited
            Dublin 4
            Ireland
            Número de IVA: IE1234567X
            21,99 €
            18,17 €
            3,82 €
            21,99 €
            """;
        var g = LectorTicket.Analizar(texto, miNIF: "00000000T", miNombre: "Tu Nombre Apellido");
        Assert.Equal("Proveedor Nube Limited", g.Proveedor);
        Assert.Equal("IE1234567X", g.NifProveedor);
        Assert.True(g.ProveedorExtranjero);
        Assert.Equal("1234567890-1", g.NumeroFactura);
        Assert.Equal(Ayuda.F(2026, 9, 16), g.Fecha);
        Assert.Equal(18.17m, g.Base);
        Assert.Equal(3.82m, g.CuotaIVA);
        Assert.Equal(21, g.TipoIVA);
        Assert.True(g.AMiNombre);
    }

    [Fact]
    public void FechasConMesEnLetra()
    {
        Assert.Equal(Ayuda.F(2026, 3, 3), LectorTicket.Analizar("Fecha: 3 de marzo de 2026").Fecha);
        Assert.Equal(Ayuda.F(2026, 4, 16), LectorTicket.Analizar("Invoice date Apr 16, 2026").Fecha);
        Assert.Equal(Ayuda.F(2026, 4, 16), LectorTicket.Analizar("16 abr. 2026").Fecha);
    }

    [Fact]
    public void VariantesDelNumero()
    {
        Assert.Equal("F-2026/0815", LectorTicket.Analizar("FACTURA N: F-2026/0815").NumeroFactura);
        Assert.Equal("123", LectorTicket.Analizar("Número de factura: 123").NumeroFactura);
        Assert.Equal("2026-77", LectorTicket.Analizar("Nº factura 2026-77").NumeroFactura);
        Assert.Equal("", LectorTicket.Analizar("Factura de compra").NumeroFactura);
    }

    [Fact]
    public void TextoVacio() => Assert.Equal(new GastoLeido(), LectorTicket.Analizar(""));
}
