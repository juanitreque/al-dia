using AlDia.Core;

namespace AlDia.Core.Tests;

public class CodigoPostalTests
{
    [Fact]
    public void ProvinciaPorPrefijo()
    {
        Assert.Equal("Madrid", CodigoPostal.Provincia("28001"));
        Assert.Equal("Málaga", CodigoPostal.Provincia("29600"));
        Assert.Equal("Bizkaia", CodigoPostal.Provincia("48001"));
        Assert.Equal("Melilla", CodigoPostal.Provincia("52001"));
    }

    [Fact]
    public void CodigosNoValidos()
    {
        Assert.Null(CodigoPostal.Provincia("53000"));
        Assert.Null(CodigoPostal.Provincia("00100"));
        Assert.Null(CodigoPostal.Provincia("2800"));
        Assert.Null(CodigoPostal.Provincia("28O01"));
    }
}

public class IBANTests
{
    // IBAN de ejemplo publicado en la documentación del estándar (no es una cuenta real)
    private const string Ejemplo = "ES9121000418450200051332";

    [Fact]
    public void Formatea()
    {
        Assert.Equal("ES91 2100 0418 4502 0005 1332", IBAN.Formatear("es91 2100-0418 450200051332"));
        Assert.Equal("ES91 2", IBAN.Formatear("ES912"));
        Assert.Equal("", IBAN.Formatear(""));
    }

    [Fact]
    public void ValidaDigitosDeControl()
    {
        Assert.True(IBAN.EsValido(Ejemplo));
        Assert.True(IBAN.EsValido("ES91 2100 0418 4502 0005 1332"));
        Assert.True(IBAN.EsValido("GB82WEST12345698765432"));
        Assert.False(IBAN.EsValido("ES9121000418450200051333"));  // un dígito cambiado
        Assert.False(IBAN.EsValido("ES912100041845020005133"));   // longitud incorrecta para España
        Assert.False(IBAN.EsValido("ES00 0000 0000 0000 0000 0000"));
    }
}

public class PieFacturaTests
{
    [Fact]
    public void RellenaMarcadores()
    {
        var texto = PieFactura.Rellenar("{nombre} ({nif}) · {domicilio} · {email}",
                                        "Tu Nombre", "00000000T", "C/ Mayor, 1, 28001 Madrid", "tu@correo.es");
        Assert.Equal("Tu Nombre (00000000T) · C/ Mayor, 1, 28001 Madrid · tu@correo.es", texto);
        Assert.Contains("{email}", PieFactura.ClausulaRGPD);
    }
}
