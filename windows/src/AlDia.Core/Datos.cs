namespace AlDia.Core;

// MARK: Códigos postales

public static class CodigoPostal
{
    /// <summary>Las dos primeras cifras del código postal español identifican la provincia (01 a 52).</summary>
    private static readonly string[] Provincias =
    [
        "Araba/Álava", "Albacete", "Alicante/Alacant", "Almería", "Ávila", "Badajoz", "Illes Balears", "Barcelona",
        "Burgos", "Cáceres", "Cádiz", "Castellón/Castelló", "Ciudad Real", "Córdoba", "A Coruña", "Cuenca", "Girona",
        "Granada", "Guadalajara", "Gipuzkoa", "Huelva", "Huesca", "Jaén", "León", "Lleida", "La Rioja", "Lugo",
        "Madrid", "Málaga", "Murcia", "Navarra", "Ourense", "Asturias", "Palencia", "Las Palmas", "Pontevedra",
        "Salamanca", "Santa Cruz de Tenerife", "Cantabria", "Segovia", "Sevilla", "Soria", "Tarragona", "Teruel",
        "Toledo", "Valencia/València", "Valladolid", "Bizkaia", "Zamora", "Zaragoza", "Ceuta", "Melilla",
    ];

    /// <summary>Cinco cifras con un prefijo de provincia existente.</summary>
    public static bool EsValido(string cp) => Provincia(cp) is not null;

    public static string? Provincia(string cp)
    {
        var limpio = cp.Trim();
        if (limpio.Length != 5 || !limpio.All(char.IsAsciiDigit)) return null;
        var n = int.Parse(limpio.AsSpan(0, 2));
        return n is >= 1 and <= 52 ? Provincias[n - 1] : null;
    }
}

// MARK: IBAN

public static class IBAN
{
    /// <summary>Longitud por país de los más habituales; para el resto basta con 15–34 caracteres.</summary>
    private static readonly Dictionary<string, int> Longitudes = new()
    {
        ["ES"] = 24, ["AD"] = 24, ["PT"] = 25, ["FR"] = 27, ["MC"] = 27, ["IT"] = 27, ["DE"] = 22, ["GB"] = 22, ["IE"] = 22,
        ["NL"] = 18, ["BE"] = 16, ["LU"] = 20, ["AT"] = 20, ["CH"] = 21, ["DK"] = 18, ["SE"] = 24, ["NO"] = 15, ["FI"] = 18, ["PL"] = 28,
    };

    /// <summary>Solo letras y cifras, en mayúsculas ("es91 2100-0418…" → "ES9121000418…").</summary>
    public static string Normalizar(string texto) =>
        new(texto.ToUpperInvariant().Where(c => char.IsAsciiLetter(c) || char.IsAsciiDigit(c)).ToArray());

    /// <summary>Grupos de cuatro: "ES91 2100 0418 4502 0005 1332".</summary>
    public static string Formatear(string texto) => string.Join(" ", Normalizar(texto).Chunk(4).Select(g => new string(g)));

    /// <summary>Longitud correcta para el país y dígitos de control válidos (ISO 13616, módulo 97).</summary>
    public static bool EsValido(string texto)
    {
        var iban = Normalizar(texto);
        if (iban.Length is < 15 or > 34
            || !char.IsAsciiLetter(iban[0]) || !char.IsAsciiLetter(iban[1])
            || !char.IsAsciiDigit(iban[2]) || !char.IsAsciiDigit(iban[3]))
            return false;
        if (Longitudes.TryGetValue(iban[..2], out var longitud) && iban.Length != longitud) return false;
        var resto = 0;
        foreach (var c in iban[4..] + iban[..4])
        {
            var valor = char.IsAsciiDigit(c) ? c - '0' : c - 'A' + 10;  // A = 10 … Z = 35
            resto = valor >= 10 ? (resto * 100 + valor) % 97 : (resto * 10 + valor) % 97;
        }
        return resto == 1;
    }
}

// MARK: Pie de factura

public static class PieFactura
{
    /// <summary>Cláusula informativa de protección de datos (art. 13 RGPD) con marcadores que se
    /// sustituyen por los datos del emisor al generar cada factura.</summary>
    public const string ClausulaRGPD =
        "Responsable del tratamiento: {nombre}, NIF {nif}, {domicilio}. Finalidad: gestionar la facturación y la relación " +
        "comercial. Base jurídica: ejecución del contrato y cumplimiento de obligaciones legales (art. 6.1.b y c RGPD). " +
        "Conservación: durante los plazos legales fiscales y mercantiles. Destinatarios: Administración Tributaria y " +
        "asesoría fiscal cuando sea necesario. Puede ejercer sus derechos de acceso, rectificación, supresión, oposición, " +
        "limitación y portabilidad escribiendo a {email}, y reclamar ante la AEPD (www.aepd.es).";

    /// <summary>Sustituye {nombre}, {nif}, {domicilio} y {email}.</summary>
    public static string Rellenar(string plantilla, string nombre, string nif, string domicilio, string email) =>
        plantilla.Replace("{nombre}", nombre).Replace("{nif}", nif).Replace("{domicilio}", domicilio).Replace("{email}", email);
}
