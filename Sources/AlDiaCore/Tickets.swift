import Foundation

// MARK: - Lectura de tickets y facturas de compra (texto de un PDF o reconocido en una foto)

/// Datos de un gasto deducidos del texto de su ticket o factura. Todo es orientativo:
/// el usuario lo revisa en el editor antes de guardar.
public struct GastoLeido: Sendable, Equatable {
    public var proveedor: String = ""
    public var nifProveedor: String = ""
    public var numeroFactura: String = ""
    public var fecha: Date?
    public var base: Decimal = 0
    public var tipoIVA: Decimal = 21
    public var cuotaIVA: Decimal = 0
    /// El documento incluye el NIF del usuario: es una factura completa a su nombre.
    public var aMiNombre: Bool = false

    public init() {}

    public var total: Decimal { base + cuotaIVA }
}

public enum LectorTicket {
    private static let importe = #"\d{1,3}(?:[.\s]\d{3})*,\d{2}|\d+[.,]\d{2}"#
    /// NIF de persona física, NIE o CIF de sociedad, con o sin guion/espacio tras la letra inicial.
    private static let nif = #"\b(?:[ABCDEFGHJNPQRSUVW][-\s]?\d{7}[0-9A-J]|\d{8}[-\s]?[A-Z]|[XYZ][-\s]?\d{7}[-\s]?[A-Z])\b"#

    /// - `miNIF`: NIF del usuario, para no confundirlo con el del proveedor y detectar si la factura es a su nombre.
    public static func analizar(_ texto: String, miNIF: String = "") -> GastoLeido {
        var r = GastoLeido()
        let lineas = texto.components(separatedBy: .newlines)
            .map { $0.trimmingCharacters(in: .whitespaces) }
            .filter { !$0.isEmpty }
        let mio = normalizarNIF(miNIF)

        // NIF: el primero que no sea el del usuario
        let nifs = coincidencias(nif, en: texto.uppercased()).map(normalizarNIF)
        r.aMiNombre = !mio.isEmpty && nifs.contains(mio)
        r.nifProveedor = nifs.first { $0 != mio } ?? ""

        // Proveedor: primera línea con texto que no sea una etiqueta habitual
        let etiquetas = ["factura", "ticket", "fecha", "nif", "cif", "n.i.f", "c.i.f", "tel", "www", "http", "@", "simplificada"]
        r.proveedor = lineas.first { l in
            let minus = l.lowercased()
            return l.count >= 3 && l.rangeOfCharacter(from: .letters) != nil
                && !etiquetas.contains(where: minus.contains)
        } ?? ""

        r.fecha = fecha(en: lineas)
        r.numeroFactura = primerGrupo(
            #"(?i)(?:n[º°o]\.?\s*(?:de\s*)?factura|n[úu]mero(?:\s*de\s*factura)?|factura|fra\.?)\s*(?:simplificada\s*)?(?:n[º°o]?\.?)?\s*[:#]?\s*((?=[A-Z0-9/\-.]*\d)[A-Z0-9][A-Z0-9/\-.]{2,})"#,
            en: texto) ?? ""

        // Tipo de IVA: el primer 21/10/4/5 % que aparezca
        if let tipo = primerGrupo(#"\b(21|10|5|4)(?:[.,]0+)?\s?%"#, en: texto), let t = Decimal(string: tipo) {
            r.tipoIVA = t
        }

        let base = importeEnLinea(lineas) { $0.contains("base") }
        let cuota = importeEnLinea(lineas) { ($0.contains("iva") || $0.contains("cuota")) && !$0.contains("total") && !$0.contains("base") }
        let total = importeEnLinea(lineas, mayor: true) { $0.contains("total") && !$0.contains("subtotal") && !$0.contains("iva") }
            ?? importeEnLinea(lineas, mayor: true) { $0.contains("total") || $0.contains("importe") || $0.contains("a pagar") }

        switch (base, cuota, total) {
        case let (b?, c?, _):
            r.base = b; r.cuotaIVA = c
        case let (b?, nil, t?):
            r.base = b; r.cuotaIVA = t - b
        case let (nil, c?, t?):
            r.base = t - c; r.cuotaIVA = c
        case let (nil, nil, t?):
            r.base = (t * 100 / (100 + r.tipoIVA)).redondeado()
            r.cuotaIVA = t - r.base
        case let (b?, nil, nil):
            r.base = b; r.cuotaIVA = Calculo.porcentaje(r.tipoIVA, de: b)
        default:
            break
        }
        return r
    }

    // MARK: Ayudas

    static func normalizarNIF(_ nif: String) -> String {
        nif.uppercased().filter { $0.isLetter || $0.isNumber }
    }

    private static func fecha(en lineas: [String]) -> Date? {
        let patron = #"\b(\d{1,2})[/.\-](\d{1,2})[/.\-](\d{2,4})\b"#
        // Primero las líneas que hablan de fecha; después cualquiera
        let ordenadas = lineas.filter { $0.lowercased().contains("fecha") } + lineas
        for linea in ordenadas {
            for m in grupos(patron, en: linea) {
                guard let d = Int(m[0]), let mes = Int(m[1]), var a = Int(m[2]), (1...12).contains(mes), (1...31).contains(d)
                else { continue }
                if a < 100 { a += 2000 }
                guard (2000...2100).contains(a) else { continue }
                if let f = Calendar.fiscal.date(from: DateComponents(year: a, month: mes, day: d)) { return f }
            }
        }
        return nil
    }

    /// Último importe de la primera línea que cumpla `condicion` (o el mayor, si `mayor`).
    /// Si la línea es solo la etiqueta, mira la siguiente.
    private static func importeEnLinea(_ lineas: [String], mayor: Bool = false, _ condicion: (String) -> Bool) -> Decimal? {
        var candidatos: [Decimal] = []
        for (i, linea) in lineas.enumerated() where condicion(linea.lowercased()) {
            var valores = coincidencias(importe, en: linea).compactMap(Importe.parse)
            if valores.isEmpty, i + 1 < lineas.count {
                valores = coincidencias(importe, en: lineas[i + 1]).compactMap(Importe.parse)
            }
            if let ultimo = valores.last {
                if !mayor { return ultimo }
                candidatos.append(ultimo)
            }
        }
        return candidatos.max()
    }

    private static func coincidencias(_ patron: String, en texto: String) -> [String] {
        guard let regex = try? NSRegularExpression(pattern: patron) else { return [] }
        return regex.matches(in: texto, range: NSRange(texto.startIndex..., in: texto)).compactMap {
            Range($0.range, in: texto).map { String(texto[$0]) }
        }
    }

    private static func primerGrupo(_ patron: String, en texto: String) -> String? {
        grupos(patron, en: texto).first?.first
    }

    private static func grupos(_ patron: String, en texto: String) -> [[String]] {
        guard let regex = try? NSRegularExpression(pattern: patron) else { return [] }
        return regex.matches(in: texto, range: NSRange(texto.startIndex..., in: texto)).map { m in
            (1..<m.numberOfRanges).map { i in Range(m.range(at: i), in: texto).map { String(texto[$0]) } ?? "" }
        }
    }
}
