using System.Globalization;
using AlDia.Core;
using AlDia.Datos;

namespace AlDia.Servicios;

public static class Ajustes
{
    public const string Nombre = "emisor.nombre";
    public const string Nif = "emisor.nif";
    public const string Direccion = "emisor.direccion";
    public const string CodigoPostal = "emisor.cp";
    public const string Poblacion = "emisor.poblacion";
    public const string Provincia = "emisor.provincia";
    public const string Email = "emisor.email";
    public const string Telefono = "emisor.telefono";
    public const string Iban = "emisor.iban";
    public const string PieFactura = "factura.pie";
    public const string EmailGestor = "gestor.email";
    public const string IvaDefecto = "defecto.iva";
    public const string RetencionDefecto = "defecto.retencion";
    /// <summary>"anual" (2027-001, por defecto) o "continuar" (sigue el formato de la última factura).</summary>
    public const string FormatoNumeracion = "numeracion.formato";
    public const string PrefijoNumeracion = "numeracion.prefijo";

    public static string Texto(string clave) => Almacen.Ajuste(clave);

    public static decimal IvaPorDefecto => decimal.TryParse(Almacen.Ajuste(IvaDefecto), CultureInfo.InvariantCulture, out var v) ? v : 21;
    public static decimal RetencionPorDefecto => decimal.TryParse(Almacen.Ajuste(RetencionDefecto), CultureInfo.InvariantCulture, out var v) ? v : 15;

    /// <summary>Número propuesto para una factura nueva según el formato elegido en Ajustes.</summary>
    public static string NumeroSugerido(DateOnly fecha, IReadOnlyList<string> anteriores, IReadOnlyList<string> existentes)
    {
        if (Almacen.Ajuste(FormatoNumeracion) == "continuar")
            return Numeracion.Siguiente(anteriores.Count == 0 ? existentes : anteriores, fecha, existentes);
        return Numeracion.Anual(fecha, existentes, Almacen.Ajuste(PrefijoNumeracion));
    }
}

/// <summary>Datos del emisor tomados de Ajustes.</summary>
public sealed class Emisor
{
    public string Nombre { get; } = Ajustes.Texto(Ajustes.Nombre);
    public string Nif { get; } = Ajustes.Texto(Ajustes.Nif);
    public string Direccion { get; } = Ajustes.Texto(Ajustes.Direccion);
    public string CodigoPostal { get; } = Ajustes.Texto(Ajustes.CodigoPostal);
    public string Poblacion { get; } = Ajustes.Texto(Ajustes.Poblacion);
    public string Provincia { get; } = Ajustes.Texto(Ajustes.Provincia);
    public string Email { get; } = Ajustes.Texto(Ajustes.Email);
    public string Telefono { get; } = Ajustes.Texto(Ajustes.Telefono);
    public string Iban { get; } = Ajustes.Texto(Ajustes.Iban);
    public string Pie { get; } = Ajustes.Texto(Ajustes.PieFactura);

    public string Localidad => Formato.Localidad(CodigoPostal, Poblacion, Provincia);

    /// <summary>Pie con los marcadores {nombre}, {nif}, {domicilio} y {email} sustituidos.</summary>
    public string PieFinal => PieFactura.Rellenar(Pie, Nombre, Nif,
        string.Join(", ", new[] { Direccion, Localidad }.Where(s => s.Length > 0)), Email);
}

/// <summary>Fecha en que la factura con programas debe cumplir VeriFactu para autónomos (RD 1007/2023, RDL 15/2025).
/// Hacienda anunció en octubre de 2026 un aplazamiento a octubre de 2028, pendiente del BOE:
/// cuando se publique, basta con cambiar esta fecha.</summary>
public static class VeriFactu
{
    public static readonly DateOnly ObligatorioDesde = new(2027, 7, 1);
    public static bool Obliga(DateOnly fecha) => fecha >= ObligatorioDesde;
}

public static class Formato
{
    public static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");

    public static string Euros(decimal v) => v.ToString("#,##0.00 €", Es);
    public static string Porcentaje(decimal v) => v.ToString("0.##", Es) + " %";
    public static string Cantidad(decimal v) => v.ToString("0.##", Es);
    public static string Fecha(DateOnly f) => f.ToString("dd/MM/yyyy", Es);
    public static string Trimestre(Trimestre t) => t.Etiqueta;

    /// <summary>"28001 Madrid" o "29600 Marbella (Málaga)": la provincia solo si no coincide con la población.</summary>
    public static string Localidad(string cp, string poblacion, string provincia)
    {
        var @base = string.Join(" ", new[] { cp, poblacion }.Where(s => s.Length > 0));
        if (provincia.Length == 0 || string.Equals(provincia, poblacion, StringComparison.CurrentCultureIgnoreCase)) return @base;
        return $"{@base} ({provincia})";
    }

    /// <summary>Nombre de archivo sin caracteres prohibidos en Windows ni tildes (los zip se ven bien en cualquier sistema).</summary>
    public static string NombreArchivo(string texto)
    {
        var sinTildes = new string(texto.Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray())
            .Normalize(System.Text.NormalizationForm.FormC);
        foreach (var c in "<>:\"/\\|?*") sinTildes = sinTildes.Replace(c, '-');
        return sinTildes.Trim();
    }
}
