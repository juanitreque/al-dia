using System.Globalization;
using System.Text.RegularExpressions;

namespace AlDia.Core;

// MARK: Numeración

public static class Numeracion
{
    /// <summary>Serie anual <c>&lt;prefijo&gt;&lt;año&gt;-&lt;NNN&gt;</c> (2027-001, F2027-001): el contador vuelve a 1 cada año
    /// y sigue al mayor número ya usado en ese año.</summary>
    public static string Anual(DateOnly fecha, IEnumerable<string> existentes, string prefijo = "", int cifras = 3)
    {
        var @base = $"{prefijo}{fecha.Year}-";
        var ultimo = existentes
            .Where(n => n.StartsWith(@base, StringComparison.Ordinal))
            .Select(n => int.TryParse(n.AsSpan(@base.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? v : (int?)null)
            .Where(v => v.HasValue)
            .Select(v => v!.Value)
            .DefaultIfEmpty(0)
            .Max();
        return @base + (ultimo + 1).ToString(CultureInfo.InvariantCulture).PadLeft(cifras, '0');
    }

    /// <summary>Propone el número de la siguiente factura continuando el formato de la última (ordenadas por fecha).
    /// <list type="bullet">
    /// <item><c>&lt;letras&gt;MMAA</c> (F0926) → mismo prefijo con el mes y año de <paramref name="fecha"/>.</item>
    /// <item>Con el año dentro (2026-087, F2026/087) → si <paramref name="fecha"/> es de otro año, ese año y contador a 1.</item>
    /// <item>Cualquier otro que acabe en dígitos (A099) → incrementa conservando los ceros.</item>
    /// </list>
    /// <paramref name="existentes"/>: todos los números ya usados, para no repetir ninguno.</summary>
    public static string Siguiente(IReadOnlyList<string> despuesDe, DateOnly fecha, IEnumerable<string>? existentes = null)
    {
        var ultimo = despuesDe.LastOrDefault(n => n.Length > 0);
        if (ultimo is null) return "";
        var usados = new HashSet<string>(despuesDe.Concat(existentes ?? []));

        var m = Regex.Match(ultimo, @"^(.*?)(20\d{2})([-/._])(\d+)\z");
        if (m.Success && int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) != fecha.Year)
            return $"{m.Groups[1].Value}{fecha.Year}{m.Groups[3].Value}" + "1".PadLeft(m.Groups[4].Length, '0');

        m = Regex.Match(ultimo, @"^([A-Za-z]*)(\d{2})(\d{2})\z");
        if (m.Success && int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) is >= 1 and <= 12)
        {
            var @base = $"{m.Groups[1].Value}{fecha.Month:00}{fecha.Year % 100:00}";
            var candidato = @base;
            for (var n = 2; usados.Contains(candidato); n++) candidato = $"{@base}-{n}";
            return candidato;
        }

        m = Regex.Match(ultimo, @"^(.*?)(\d+)\z");
        if (m.Success && long.TryParse(m.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var valor))
        {
            var prefijo = m.Groups[1].Value;
            var ancho = m.Groups[2].Length;
            string Formato(long n) => prefijo + n.ToString(CultureInfo.InvariantCulture).PadLeft(ancho, '0');
            var n = valor + 1;
            while (usados.Contains(Formato(n))) n++;
            return Formato(n);
        }
        return "";
    }
}

// MARK: Lectura de facturas en PDF (plantilla con "Número: … Fecha: …" y tabla CONCEPTO/UNIDAD/PRECIO)

public sealed record LineaLeida(string Concepto, decimal Cantidad, decimal Precio, decimal Importe);

public sealed record FacturaLeida(
    string Numero,
    DateOnly Fecha,
    string ClienteNombre,
    string ClienteNIF,
    IReadOnlyList<LineaLeida> Lineas,
    decimal Base,
    decimal TipoIVA,
    decimal CuotaIVA,
    decimal TipoRetencion,
    decimal Retencion,
    decimal Total)
{
    /// <summary>Incoherencias detectadas al leer (vacío = todo cuadra).</summary>
    public IReadOnlyList<string> Avisos
    {
        get
        {
            var avisos = new List<string>();
            if (ClienteNIF.Length == 0) avisos.Add("No se encontró el NIF del cliente");
            if (Lineas.Count == 0) avisos.Add("No se encontraron líneas de detalle");
            if (Lineas.Count > 0 && Lineas.Sum(l => l.Importe) != Base) avisos.Add("Las líneas no suman la base");
            if (Calculo.Porcentaje(TipoIVA, Base) != CuotaIVA) avisos.Add("La cuota de IVA no corresponde al tipo");
            if (Base + CuotaIVA - Retencion != Total) avisos.Add("Base + IVA − retención no da el total");
            return avisos;
        }
    }
}

public static class LectorFactura
{
    private const string Imp = @"-?(?:\d{1,3}(?:\.\d{3})+|\d+),\d{2}";
    private const string Ent = @"\d+(?:,\d+)?";

    /// <summary>Analiza el texto extraído de un PDF de factura. Devuelve null si no encuentra número y fecha.</summary>
    public static FacturaLeida? Analizar(string texto)
    {
        var lineas = Texto.Lineas(texto);

        // "Número: 2026-001 Fecha: 31/01/2026 B12345678"
        var cabecera = Buscar(lineas, @"N[úu]mero:\s*(\S+)\s+Fecha:\s*(\d{1,2})/(\d{1,2})/(\d{4})(?:\s+([A-Z0-9]{8,10}))?");
        if (cabecera is null) return null;
        DateOnly fecha;
        try
        {
            fecha = new DateOnly(int.Parse(cabecera[3], CultureInfo.InvariantCulture),
                                 int.Parse(cabecera[2], CultureInfo.InvariantCulture),
                                 int.Parse(cabecera[1], CultureInfo.InvariantCulture));
        }
        catch (ArgumentOutOfRangeException) { return null; }

        var cliente = "";
        var i = lineas.FindIndex(l => l.Contains("DATOS CLIENTE", StringComparison.CurrentCultureIgnoreCase));
        if (i >= 0 && i + 1 < lineas.Count) cliente = lineas[i + 1];

        // Detalle entre la cabecera "CONCEPTO …" y "Base …": "PERSONAL(2) 3 35,00 105,00"
        var detalle = new List<LineaLeida>();
        var desde = lineas.FindIndex(l => l.ToUpperInvariant().StartsWith("CONCEPTO", StringComparison.Ordinal));
        if (desde >= 0)
        {
            var patron = $@"^(.+?)\s+({Ent})\s+({Imp})\s+({Imp})$";
            foreach (var linea in lineas.Skip(desde + 1))
            {
                if (linea.StartsWith("Base", StringComparison.Ordinal)) break;
                if (Grupos(linea, patron) is not { } g) continue;
                detalle.Add(new LineaLeida(g[0], Num(g[1]), Num(g[2]), Num(g[3])));
            }
        }

        // "485,00 21 485,00 15" (base, % IVA, base IRPF, % IRPF)
        var tipos = Buscar(lineas, $@"^({Imp})\s+({Ent})\s+({Imp})\s+({Ent})$");
        // "485,00 101,85 -72,75 514,10" (base, IVA, −IRPF, líquido)
        var totales = Buscar(lineas, $@"^({Imp})\s+({Imp})\s+({Imp})\s+({Imp})$");

        var @base = totales is not null ? Num(totales[0]) : tipos is not null ? Num(tipos[0]) : detalle.Sum(l => l.Importe);
        var tipoIVA = tipos is not null ? Num(tipos[1]) : 21;
        var tipoRetencion = tipos is not null ? Num(tipos[3]) : 0;
        var cuota = totales is not null ? Num(totales[1]) : Calculo.Porcentaje(tipoIVA, @base);
        var retencion = totales is not null ? Math.Abs(Num(totales[2])) : Calculo.Porcentaje(tipoRetencion, @base);
        var total = totales is not null ? Num(totales[3]) : @base + cuota - retencion;

        return new FacturaLeida(cabecera[0], fecha, cliente, cabecera[4], detalle, @base, tipoIVA, cuota,
                                tipoRetencion, retencion, total);
    }

    private static decimal Num(string texto) => Importe.Parse(texto) ?? 0;

    private static string[]? Buscar(IEnumerable<string> lineas, string patron)
    {
        foreach (var linea in lineas)
            if (Grupos(linea, patron) is { } g) return g;
        return null;
    }

    /// <summary>Grupos de captura de la primera coincidencia ("" para los que no participan).</summary>
    private static string[]? Grupos(string texto, string patron)
    {
        var m = Regex.Match(texto, patron);
        if (!m.Success) return null;
        return Enumerable.Range(1, m.Groups.Count - 1).Select(i => m.Groups[i].Success ? m.Groups[i].Value : "").ToArray();
    }
}

// MARK: Ficheros de importación de la aplicación gratuita VERI*FACTU de la AEAT

/// <summary>Formatos de texto plano separados por "#" que acepta la app de la AEAT en
/// Otros servicios → Clientes / Productos → Importar.</summary>
public static class ExportacionAEAT
{
    public sealed record Cliente(string Nombre, string Nif, string Direccion = "", string CodigoPostal = "",
                                 string Poblacion = "", string Provincia = "", string Pais = "ES",
                                 string Email = "", string Telefono = "");

    public sealed record Producto(string Id, string Descripcion, decimal Precio, decimal TipoIVA);

    public const string CabeceraClientes =
        "Nombre o Razón Social#NIF#Dirección#Código Postal#Población#Provincia#Símbolo País#Email#Teléfono#Web";
    public const string CabeceraProductos =
        "ID#Descripción#Precio Unitario#Concepto Descuento#Importe Descuento#Código IVA/IPSI/IGIC#Código Clave Régimen#Código de Calificación Operación u Operación Exenta#Tipo Impositivo#Tipo Recargo de Equivalencia";

    public static string Clientes(IEnumerable<Cliente> clientes)
    {
        var filas = clientes.Select(c => string.Join("#",
            new[] { c.Nombre, c.Nif, c.Direccion, c.CodigoPostal, c.Poblacion, c.Provincia, c.Pais, c.Email, c.Telefono, "" }
                .Select(Campo)));
        return string.Join("\r\n", filas.Prepend(CabeceraClientes)) + "\r\n";
    }

    /// <summary>Productos sujetos a IVA en régimen general: impuesto 01 (IVA), clave de régimen 01 (general),
    /// calificación S1 (sujeta y no exenta). Precio con punto decimal.</summary>
    public static string Productos(IEnumerable<Producto> productos)
    {
        var filas = productos.Select(p =>
        {
            var sujeto = p.TipoIVA > 0;
            return string.Join("#", new[]
            {
                Identificador(p.Id), p.Descripcion, Decimal(p.Precio), "", "",
                sujeto ? "01" : "", sujeto ? "01" : "", sujeto ? "S1" : "", sujeto ? Decimal(p.TipoIVA) : "", "",
            }.Select(Campo));
        });
        return string.Join("\r\n", filas.Prepend(CabeceraProductos)) + "\r\n";
    }

    /// <summary>El ID de producto no admite espacios.</summary>
    public static string Identificador(string texto) => texto.Trim().Replace(' ', '_');

    internal static string Campo(string texto) =>
        string.Join(" ", texto.Replace('#', ' ').Split(["\r\n", "\n", "\r"], StringSplitOptions.None)).Trim();

    internal static string Decimal(decimal valor) => valor.ToString("0.00", CultureInfo.InvariantCulture);
}

/// <summary>Utilidades de texto compartidas por los lectores.</summary>
internal static class Texto
{
    /// <summary>Líneas no vacías y recortadas.</summary>
    public static List<string> Lineas(string texto) =>
        texto.Split(["\r\n", "\n", "\r"], StringSplitOptions.None)
             .Select(l => l.Trim())
             .Where(l => l.Length > 0)
             .ToList();
}
