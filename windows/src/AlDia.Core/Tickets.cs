using System.Globalization;
using System.Text.RegularExpressions;

namespace AlDia.Core;

// MARK: Lectura de tickets y facturas de compra (texto de un PDF o reconocido en una foto)

/// <summary>Datos de un gasto deducidos del texto de su ticket o factura. Todo es orientativo:
/// el usuario lo revisa en el editor antes de guardar.</summary>
public sealed record GastoLeido
{
    public string Proveedor { get; set; } = "";
    public string NifProveedor { get; set; } = "";
    public string NumeroFactura { get; set; } = "";
    public DateOnly? Fecha { get; set; }
    public decimal Base { get; set; }
    public decimal TipoIVA { get; set; } = 21;
    public decimal CuotaIVA { get; set; }
    /// <summary>El documento incluye el NIF del usuario: es una factura completa a su nombre.</summary>
    public bool AMiNombre { get; set; }
    /// <summary>El NIF del proveedor es un número de IVA de otro país de la UE (IE…, DE…, FR…).</summary>
    public bool ProveedorExtranjero { get; set; }

    public decimal Total => Base + CuotaIVA;
}

public static class LectorTicket
{
    private const string Importe = @"\d{1,3}(?:[.\s]\d{3})*,\d{2}|\d+[.,]\d{2}";
    /// <summary>NIF de persona física, NIE o CIF de sociedad, con o sin guion/espacio tras la letra inicial.</summary>
    private const string Nif = @"\b(?:[ABCDEFGHJNPQRSUVW][-\s]?\d{7}[0-9A-J]|\d{8}[-\s]?[A-Z]|[XYZ][-\s]?\d{7}[-\s]?[A-Z])\b";
    /// <summary>Número de IVA intracomunitario: prefijo de país de la UE + 8 a 12 caracteres (con al menos 6 cifras).</summary>
    private const string NifUE = @"\b(?:AT|BE|BG|CY|CZ|DE|DK|EE|EL|ES|FI|FR|HR|HU|IE|IT|LT|LU|LV|MT|NL|PL|PT|RO|SE|SI|SK)\s?[0-9A-Z]{8,12}\b";
    /// <summary>Forma jurídica al final de una línea: "Tienda Ejemplo, S.L.", "Proveedor Nube Limited"…</summary>
    private const string FormaJuridica = @"(?i)\b(?:S\.?\s?L\.?\s?U?|S\.?\s?A\.?\s?U?|S\.?\s?L\.?\s?L\.?|S\.?\s?C\.?|C\.?\s?B\.?|Limited|Ltd|Inc|GmbH|B\.?V|LLC|SAS|S\.?r\.?l|Corp(?:oration)?)\.?\s*$";
    private const string NumeroFactura = @"(?i)(?:n[º°o]\.?\s*(?:de\s*)?factura|n[úu]mero(?:\s*de\s*factura)?|factura|fra\.?)\s*(?:simplificada\s*)?(?:n[º°o]?\.?)?\s*[:#]?\s*((?=[A-Z0-9/\-.]*\d)[A-Z0-9][A-Z0-9/\-.]{2,})";
    /// <summary>Etiquetas tras las que viene el cliente (el usuario), no el proveedor.</summary>
    private static readonly string[] BloqueCliente = ["facturar a", "cliente", "destinatario", "bill to", "billed to", "datos del cliente"];
    private static readonly decimal[] Tipos = [21, 10, 5, 4];

    /// <param name="miNIF">NIF del usuario, para no confundirlo con el del proveedor y detectar si la factura es a su nombre.</param>
    /// <param name="miNombre">Nombre del usuario, para no tomarlo por el proveedor.</param>
    public static GastoLeido Analizar(string texto, string miNIF = "", string miNombre = "")
    {
        var r = new GastoLeido();
        var lineas = Texto.Lineas(texto);
        var mio = NormalizarNIF(miNIF);
        var mios = mio.Length == 0 ? new HashSet<string>() : [mio, "ES" + mio];

        // NIF: el primero español que no sea el del usuario; si no hay, uno de IVA de la UE
        var mayusculas = texto.ToUpperInvariant();
        var nifsES = Coincidencias(Nif, mayusculas).Select(NormalizarNIF).ToList();
        var nifsUE = Coincidencias(NifUE, mayusculas).Select(NormalizarNIF)
            .Where(n => n.Count(char.IsDigit) >= 6).ToList();
        r.AMiNombre = mios.Count > 0 && nifsES.Concat(nifsUE).Any(mios.Contains);
        if (nifsES.FirstOrDefault(n => !mios.Contains(n)) is { } espanol)
        {
            r.NifProveedor = espanol;
        }
        else if (nifsUE.FirstOrDefault(n => !mios.Contains(n) && !mios.Contains(n[2..])) is { } europeo)
        {
            r.NifProveedor = europeo;
            r.ProveedorExtranjero = !europeo.StartsWith("ES", StringComparison.Ordinal);
        }

        r.Proveedor = Proveedor(lineas, miNombre);
        r.Fecha = Fecha(lineas);
        r.NumeroFactura = PrimerGrupo(NumeroFactura, texto) ?? "";

        // Tipo de IVA: el primer 21/10/4/5 % que aparezca
        if (PrimerGrupo(@"\b(21|10|5|4)(?:[.,]0+)?\s?%", texto) is { } tipo)
            r.TipoIVA = decimal.Parse(tipo, CultureInfo.InvariantCulture);

        // Importes por etiqueta ("I.V.A." se compara como "iva")
        var @base = ImporteEnLinea(lineas, false, l => l.Contains("base") || l.Contains("subtotal") || l.Contains("neto"));
        var cuota = ImporteEnLinea(lineas, false, l => (l.Contains("iva") || l.Contains("cuota")) && !l.Contains("total") && !l.Contains("base"));
        var total = ImporteEnLinea(lineas, true, l => l.Contains("total") && !l.Contains("subtotal") && !l.Contains("iva"))
                    ?? ImporteEnLinea(lineas, true, l => l.Contains("total") || l.Contains("importe") || l.Contains("a pagar"));

        if (@base is { } b0 && cuota is { } c0 && TipoQueCuadra(b0, c0) is not null)
        {
            r.Base = b0; r.CuotaIVA = c0;
        }
        else if (TernaCoherente(texto) is { } terna)
        {
            // Etiquetas e importes en columnas separadas: base + cuota = total entre las cifras del documento
            r.Base = terna.Base; r.CuotaIVA = terna.Cuota; r.TipoIVA = terna.Tipo;
        }
        else
        {
            switch (@base, cuota, total)
            {
                case ({ } b, { } c, _):
                    r.Base = b; r.CuotaIVA = c;
                    break;
                case ({ } b, null, { } t):
                    r.Base = b; r.CuotaIVA = t - b;
                    break;
                case (null, { } c, { } t):
                    r.Base = t - c; r.CuotaIVA = c;
                    break;
                case (null, null, { } t):
                    r.Base = Calculo.Redondear(t * 100 / (100 + r.TipoIVA));
                    r.CuotaIVA = t - r.Base;
                    break;
                case ({ } b, null, null):
                    r.Base = b; r.CuotaIVA = Calculo.Porcentaje(r.TipoIVA, b);
                    break;
            }
        }
        return r;
    }

    // MARK: Ayudas

    internal static string NormalizarNIF(string nif) =>
        new(nif.ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());

    /// <summary>Tipo (21, 10, 5 o 4) del que <paramref name="cuota"/> es la cuota de <paramref name="b"/>, con ±0,01 de margen.</summary>
    private static decimal? TipoQueCuadra(decimal b, decimal cuota)
    {
        foreach (var t in Tipos)
            if (Math.Abs(Calculo.Porcentaje(t, b) - cuota) <= 0.01m) return t;
        return null;
    }

    /// <summary>Texto de una línea preparado para buscar etiquetas: minúsculas y sin puntos ("I.V.A." → "iva").</summary>
    private static string Etiqueta(string linea) => linea.ToLowerInvariant().Replace(".", "");

    private static string Proveedor(List<string> lineas, string miNombre)
    {
        var nombre = miNombre.Trim().ToLowerInvariant();
        // Líneas del bloque del cliente: la etiqueta y las 4 siguientes
        var excluidas = new HashSet<int>();
        for (var i = 0; i < lineas.Count; i++)
        {
            var minus = lineas[i].ToLowerInvariant();
            if (BloqueCliente.Any(minus.Contains))
                for (var j = i; j <= Math.Min(i + 4, lineas.Count - 1); j++) excluidas.Add(j);
        }
        string[] etiquetas = ["factura", "ticket", "fecha", "nif", "cif", "n.i.f", "c.i.f", "tel", "www", "http", "@",
                              "simplificada", "iva", "total", "página", "page"];
        var candidatas = lineas.Where((l, i) =>
        {
            var minus = l.ToLowerInvariant();
            return !excluidas.Contains(i) && l.Length >= 3 && l.Any(char.IsLetter)
                && !etiquetas.Any(minus.Contains)
                && (nombre.Length == 0 || !minus.Contains(nombre));
        }).ToList();
        return candidatas.FirstOrDefault(l => Regex.IsMatch(l, FormaJuridica)) ?? candidatas.FirstOrDefault() ?? "";
    }

    /// <summary>Busca entre todas las cifras del documento una base y una cuota que sumen otra cifra
    /// y cuya cuota sea el 21, 10, 5 o 4 % de la base. Se queda con la de mayor total.</summary>
    private static (decimal Base, decimal Cuota, decimal Tipo)? TernaCoherente(string texto)
    {
        var cifras = Coincidencias(Importe, texto)
            .Select(AlDia.Core.Importe.Parse).Where(v => v is > 0).Select(v => v!.Value).Distinct().ToList();
        if (cifras.Count is < 3 or > 200) return null;
        var conjunto = cifras.ToHashSet();
        (decimal Base, decimal Cuota, decimal Tipo)? mejor = null;
        foreach (var b in cifras)
        foreach (var c in cifras)
        {
            if (c >= b || !conjunto.Contains(b + c)) continue;
            if (TipoQueCuadra(b, c) is not { } tipo) continue;
            if (mejor is null || b + c > mejor.Value.Base + mejor.Value.Cuota) mejor = (b, c, tipo);
        }
        return mejor;
    }

    private static readonly Dictionary<string, int> Meses = new()
    {
        ["ene"] = 1, ["jan"] = 1, ["feb"] = 2, ["mar"] = 3, ["abr"] = 4, ["apr"] = 4, ["may"] = 5, ["jun"] = 6, ["jul"] = 7,
        ["ago"] = 8, ["aug"] = 8, ["sep"] = 9, ["oct"] = 10, ["nov"] = 11, ["dic"] = 12, ["dec"] = 12,
    };

    private static DateOnly? Fecha(List<string> lineas)
    {
        // Primero las líneas con "fecha"/"date" y las dos siguientes (el valor suele ir debajo); después cualquiera
        var ordenadas = new List<string>();
        for (var i = 0; i < lineas.Count; i++)
        {
            var minus = lineas[i].ToLowerInvariant();
            if (minus.Contains("fecha") || minus.Contains("date"))
                ordenadas.AddRange(lineas.Skip(i).Take(3));
        }
        ordenadas.AddRange(lineas);
        foreach (var linea in ordenadas)
            if ((FechaNumerica(linea) ?? FechaConMes(linea)) is { } f) return f;
        return null;
    }

    /// <summary>16/04/2026, 16-04-26, 16.04.2026</summary>
    private static DateOnly? FechaNumerica(string linea)
    {
        foreach (var m in Grupos(@"\b(\d{1,2})[/.\-](\d{1,2})[/.\-](\d{2,4})\b", linea))
            if (Componer(Entero(m[0]), Entero(m[1]), Entero(m[2])) is { } f) return f;
        return null;
    }

    /// <summary>16 abr 2026, 16 de abril de 2026, 16 sept. 2026, Apr 16, 2026</summary>
    private static DateOnly? FechaConMes(string linea)
    {
        const string mes = @"(ene|feb|mar|abr|may|jun|jul|ago|sep|oct|nov|dic|jan|apr|aug|dec)[a-záéíóú]*\.?";
        foreach (var m in Grupos($@"(?i)\b(\d{{1,2}})\s+(?:de\s+)?{mes}\s+(?:de\s+)?(\d{{4}})\b", linea))
            if (Componer(Entero(m[0]), Meses.GetValueOrDefault(m[1].ToLowerInvariant()), Entero(m[2])) is { } f) return f;
        foreach (var m in Grupos($@"(?i)\b{mes}\s+(\d{{1,2}}),?\s+(\d{{4}})\b", linea))
            if (Componer(Entero(m[1]), Meses.GetValueOrDefault(m[0].ToLowerInvariant()), Entero(m[2])) is { } f) return f;
        return null;
    }

    private static int Entero(string s) => int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? v : 0;

    private static DateOnly? Componer(int dia, int mes, int año)
    {
        if (mes is < 1 or > 12 || dia is < 1 or > 31) return null;
        if (año < 100) año += 2000;
        if (año is < 2000 or > 2100 || dia > DateTime.DaysInMonth(año, mes)) return null;
        return new DateOnly(año, mes, dia);
    }

    /// <summary>Último importe de la primera línea que cumpla <paramref name="condicion"/> (o el mayor, si <paramref name="mayor"/>).
    /// Si la línea es solo la etiqueta, mira la siguiente.</summary>
    private static decimal? ImporteEnLinea(List<string> lineas, bool mayor, Func<string, bool> condicion)
    {
        var candidatos = new List<decimal>();
        for (var i = 0; i < lineas.Count; i++)
        {
            if (!condicion(Etiqueta(lineas[i]))) continue;
            var valores = Valores(lineas[i]);
            if (valores.Count == 0 && i + 1 < lineas.Count) valores = Valores(lineas[i + 1]);
            if (valores.Count == 0) continue;
            if (!mayor) return valores[^1];
            candidatos.Add(valores[^1]);
        }
        return candidatos.Count > 0 ? candidatos.Max() : null;
    }

    private static List<decimal> Valores(string linea) =>
        Coincidencias(Importe, linea).Select(AlDia.Core.Importe.Parse).Where(v => v.HasValue).Select(v => v!.Value).ToList();

    private static IEnumerable<string> Coincidencias(string patron, string texto) =>
        Regex.Matches(texto, patron).Select(m => m.Value);

    private static string? PrimerGrupo(string patron, string texto) => Grupos(patron, texto).FirstOrDefault()?[0];

    private static IEnumerable<string[]> Grupos(string patron, string texto) =>
        Regex.Matches(texto, patron).Select(m =>
            Enumerable.Range(1, m.Groups.Count - 1).Select(i => m.Groups[i].Success ? m.Groups[i].Value : "").ToArray());
}
