import Foundation

// MARK: - Códigos postales

public enum CodigoPostal {
    /// Las dos primeras cifras del código postal español identifican la provincia (01 a 52).
    private static let provincias: [String] = [
        "Araba/Álava", "Albacete", "Alicante/Alacant", "Almería", "Ávila", "Badajoz", "Illes Balears", "Barcelona",
        "Burgos", "Cáceres", "Cádiz", "Castellón/Castelló", "Ciudad Real", "Córdoba", "A Coruña", "Cuenca", "Girona",
        "Granada", "Guadalajara", "Gipuzkoa", "Huelva", "Huesca", "Jaén", "León", "Lleida", "La Rioja", "Lugo",
        "Madrid", "Málaga", "Murcia", "Navarra", "Ourense", "Asturias", "Palencia", "Las Palmas", "Pontevedra",
        "Salamanca", "Santa Cruz de Tenerife", "Cantabria", "Segovia", "Sevilla", "Soria", "Tarragona", "Teruel",
        "Toledo", "Valencia/València", "Valladolid", "Bizkaia", "Zamora", "Zaragoza", "Ceuta", "Melilla",
    ]

    /// Cinco cifras con un prefijo de provincia existente.
    public static func esValido(_ cp: String) -> Bool { provincia(cp) != nil }

    public static func provincia(_ cp: String) -> String? {
        let limpio = cp.trimmingCharacters(in: .whitespaces)
        guard limpio.count == 5, limpio.allSatisfy(\.isNumber), let n = Int(limpio.prefix(2)), (1...52).contains(n)
        else { return nil }
        return provincias[n - 1]
    }
}

// MARK: - IBAN

public enum IBAN {
    /// Longitud por país de los más habituales; para el resto basta con 15–34 caracteres.
    private static let longitudes: [String: Int] = [
        "ES": 24, "AD": 24, "PT": 25, "FR": 27, "MC": 27, "IT": 27, "DE": 22, "GB": 22, "IE": 22,
        "NL": 18, "BE": 16, "LU": 20, "AT": 20, "CH": 21, "DK": 18, "SE": 24, "NO": 15, "FI": 18, "PL": 28,
    ]

    /// Solo letras y cifras, en mayúsculas ("es91 2100-0418…" → "ES9121000418…").
    public static func normalizar(_ texto: String) -> String {
        texto.uppercased().filter { ($0.isASCII && $0.isLetter) || $0.isNumber }
    }

    /// Grupos de cuatro: "ES91 2100 0418 4502 0005 1332".
    public static func formatear(_ texto: String) -> String {
        let limpio = Array(normalizar(texto))
        return stride(from: 0, to: limpio.count, by: 4)
            .map { String(limpio[$0..<min($0 + 4, limpio.count)]) }
            .joined(separator: " ")
    }

    /// Longitud correcta para el país y dígitos de control válidos (ISO 13616, módulo 97).
    public static func esValido(_ texto: String) -> Bool {
        let iban = normalizar(texto)
        guard iban.count >= 15, iban.count <= 34,
              iban.prefix(2).allSatisfy(\.isLetter), iban.dropFirst(2).prefix(2).allSatisfy(\.isNumber)
        else { return false }
        if let longitud = longitudes[String(iban.prefix(2))], iban.count != longitud { return false }
        let reordenado = iban.dropFirst(4) + iban.prefix(4)
        var resto = 0
        for caracter in reordenado {
            let valor: Int
            if let cifra = caracter.wholeNumberValue {
                valor = cifra
            } else if let ascii = caracter.asciiValue, (65...90).contains(ascii) {
                valor = Int(ascii) - 55  // A = 10 … Z = 35
            } else {
                return false
            }
            for digito in String(valor) {
                resto = (resto * 10 + digito.wholeNumberValue!) % 97
            }
        }
        return resto == 1
    }
}

// MARK: - Pie de factura

public enum PieFactura {
    /// Cláusula informativa de protección de datos (art. 13 RGPD) con marcadores que se
    /// sustituyen por los datos del emisor al generar cada factura.
    public static let clausulaRGPD = """
    Responsable del tratamiento: {nombre}, NIF {nif}, {domicilio}. Finalidad: gestionar la facturación y la relación \
    comercial. Base jurídica: ejecución del contrato y cumplimiento de obligaciones legales (art. 6.1.b y c RGPD). \
    Conservación: durante los plazos legales fiscales y mercantiles. Destinatarios: Administración Tributaria y \
    asesoría fiscal cuando sea necesario. Puede ejercer sus derechos de acceso, rectificación, supresión, oposición, \
    limitación y portabilidad escribiendo a {email}, y reclamar ante la AEPD (www.aepd.es).
    """

    /// Sustituye {nombre}, {nif}, {domicilio} y {email}.
    public static func rellenar(_ plantilla: String, nombre: String, nif: String, domicilio: String, email: String) -> String {
        plantilla
            .replacingOccurrences(of: "{nombre}", with: nombre)
            .replacingOccurrences(of: "{nif}", with: nif)
            .replacingOccurrences(of: "{domicilio}", with: domicilio)
            .replacingOccurrences(of: "{email}", with: email)
    }
}
