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
    /// El NIF del proveedor es un número de IVA de otro país de la UE (IE…, DE…, FR…).
    public var proveedorExtranjero: Bool = false

    public init() {}

    public var total: Decimal { base + cuotaIVA }
}

public enum LectorTicket {
    private static let importe = #"\d{1,3}(?:[.\s]\d{3})*,\d{2}|\d+[.,]\d{2}"#
    /// NIF de persona física, NIE o CIF de sociedad, con o sin guion/espacio tras la letra inicial.
    private static let nif = #"\b(?:[ABCDEFGHJNPQRSUVW][-\s]?\d{7}[0-9A-J]|\d{8}[-\s]?[A-Z]|[XYZ][-\s]?\d{7}[-\s]?[A-Z])\b"#

    /// Número de IVA intracomunitario: prefijo de país de la UE + 8 a 12 caracteres (con al menos 6 cifras).
    private static let nifUE = #"\b(?:AT|BE|BG|CY|CZ|DE|DK|EE|EL|ES|FI|FR|HR|HU|IE|IT|LT|LU|LV|MT|NL|PL|PT|RO|SE|SI|SK)\s?[0-9A-Z]{8,12}\b"#
    /// Forma jurídica al final de una línea: "Tienda Ejemplo, S.L.", "Google Commerce Limited"…
    private static let formaJuridica = #"(?i)\b(?:S\.?\s?L\.?\s?U?|S\.?\s?A\.?\s?U?|S\.?\s?L\.?\s?L\.?|S\.?\s?C\.?|C\.?\s?B\.?|Limited|Ltd|Inc|GmbH|B\.?V|LLC|SAS|S\.?r\.?l|Corp(?:oration)?)\.?\s*$"#
    /// Etiquetas tras las que viene el cliente (el usuario), no el proveedor.
    private static let bloqueCliente = ["facturar a", "cliente", "destinatario", "bill to", "billed to", "datos del cliente"]

    /// - `miNIF`: NIF del usuario, para no confundirlo con el del proveedor y detectar si la factura es a su nombre.
    /// - `miNombre`: nombre del usuario, para no tomarlo por el proveedor.
    public static func analizar(_ texto: String, miNIF: String = "", miNombre: String = "") -> GastoLeido {
        var r = GastoLeido()
        let lineas = texto.components(separatedBy: .newlines)
            .map { $0.trimmingCharacters(in: .whitespaces) }
            .filter { !$0.isEmpty }
        let mio = normalizarNIF(miNIF)
        let mios: Set<String> = mio.isEmpty ? [] : [mio, "ES" + mio]

        // NIF: el primero español que no sea el del usuario; si no hay, uno de IVA de la UE
        let mayusculas = texto.uppercased()
        let nifsES = coincidencias(nif, en: mayusculas).map(normalizarNIF)
        let nifsUE = coincidencias(nifUE, en: mayusculas).map(normalizarNIF)
            .filter { $0.filter(\.isNumber).count >= 6 }
        r.aMiNombre = !mios.isEmpty && (nifsES + nifsUE).contains(where: mios.contains)
        if let espanol = nifsES.first(where: { !mios.contains($0) }) {
            r.nifProveedor = espanol
        } else if let europeo = nifsUE.first(where: { !mios.contains($0) && !mios.contains(String($0.dropFirst(2))) }) {
            r.nifProveedor = europeo
            r.proveedorExtranjero = !europeo.hasPrefix("ES")
        }

        r.proveedor = proveedor(en: lineas, miNombre: miNombre)
        r.fecha = fecha(en: lineas)
        r.numeroFactura = primerGrupo(
            #"(?i)(?:n[º°o]\.?\s*(?:de\s*)?factura|n[úu]mero(?:\s*de\s*factura)?|factura|fra\.?)\s*(?:simplificada\s*)?(?:n[º°o]?\.?)?\s*[:#]?\s*((?=[A-Z0-9/\-.]*\d)[A-Z0-9][A-Z0-9/\-.]{2,})"#,
            en: texto) ?? ""

        // Tipo de IVA: el primer 21/10/4/5 % que aparezca
        if let tipo = primerGrupo(#"\b(21|10|5|4)(?:[.,]0+)?\s?%"#, en: texto), let t = Decimal(string: tipo) {
            r.tipoIVA = t
        }

        // Importes por etiqueta ("I.V.A." se compara como "iva")
        let base = importeEnLinea(lineas) { $0.contains("base") || $0.contains("subtotal") || $0.contains("neto") }
        let cuota = importeEnLinea(lineas) { ($0.contains("iva") || $0.contains("cuota")) && !$0.contains("total") && !$0.contains("base") }
        let total = importeEnLinea(lineas, mayor: true) { $0.contains("total") && !$0.contains("subtotal") && !$0.contains("iva") }
            ?? importeEnLinea(lineas, mayor: true) { $0.contains("total") || $0.contains("importe") || $0.contains("a pagar") }

        let cuadra: (Decimal, Decimal) -> Bool = { b, c in
            [21, 10, 5, 4].contains { abs(Calculo.porcentaje($0, de: b) - c) <= Decimal(string: "0.01")! }
        }

        if let b = base, let c = cuota, cuadra(b, c) {
            r.base = b; r.cuotaIVA = c
        } else if let terna = ternaCoherente(en: texto) {
            // Etiquetas e importes en columnas separadas: base + cuota = total entre las cifras del documento
            r.base = terna.base; r.cuotaIVA = terna.cuota; r.tipoIVA = terna.tipo
        } else {
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
        }
        return r
    }

    // MARK: Ayudas

    static func normalizarNIF(_ nif: String) -> String {
        nif.uppercased().filter { $0.isLetter || $0.isNumber }
    }

    /// Texto de una línea preparado para buscar etiquetas: minúsculas y sin puntos ("I.V.A." → "iva").
    private static func etiqueta(_ linea: String) -> String {
        linea.lowercased().replacingOccurrences(of: ".", with: "")
    }

    private static func proveedor(en lineas: [String], miNombre: String) -> String {
        let nombre = miNombre.trimmingCharacters(in: .whitespaces).lowercased()
        // Líneas del bloque del cliente: la etiqueta y las 4 siguientes
        var excluidas = Set<Int>()
        for (i, linea) in lineas.enumerated() where bloqueCliente.contains(where: linea.lowercased().contains) {
            excluidas.formUnion(i...min(i + 4, lineas.count - 1))
        }
        let etiquetas = ["factura", "ticket", "fecha", "nif", "cif", "n.i.f", "c.i.f", "tel", "www", "http", "@",
                         "simplificada", "iva", "total", "página", "page"]
        let candidatas = lineas.enumerated().filter { i, l in
            let minus = l.lowercased()
            return !excluidas.contains(i) && l.count >= 3 && l.rangeOfCharacter(from: .letters) != nil
                && !etiquetas.contains(where: minus.contains)
                && (nombre.isEmpty || !minus.contains(nombre))
        }.map(\.element)
        return candidatas.first { $0.range(of: formaJuridica, options: .regularExpression) != nil }
            ?? candidatas.first ?? ""
    }

    /// Busca entre todas las cifras del documento una base y una cuota que sumen otra cifra
    /// y cuya cuota sea el 21, 10, 5 o 4 % de la base. Se queda con la de mayor total.
    private static func ternaCoherente(en texto: String) -> (base: Decimal, cuota: Decimal, tipo: Decimal)? {
        let cifras = Array(Set(coincidencias(importe, en: texto).compactMap(Importe.parse))).filter { $0 > 0 }
        guard cifras.count >= 3, cifras.count <= 200 else { return nil }
        let conjunto = Set(cifras)
        var mejor: (base: Decimal, cuota: Decimal, tipo: Decimal)?
        for b in cifras {
            for c in cifras where c < b && conjunto.contains(b + c) {
                guard let tipo = ([21, 10, 5, 4] as [Decimal]).first(where: {
                    abs(Calculo.porcentaje($0, de: b) - c) <= Decimal(string: "0.01")!
                }) else { continue }
                if mejor == nil || b + c > mejor!.base + mejor!.cuota { mejor = (b, c, tipo) }
            }
        }
        return mejor
    }

    private static let meses: [String: Int] = [
        "ene": 1, "jan": 1, "feb": 2, "mar": 3, "abr": 4, "apr": 4, "may": 5, "jun": 6, "jul": 7,
        "ago": 8, "aug": 8, "sep": 9, "oct": 10, "nov": 11, "dic": 12, "dec": 12,
    ]

    private static func fecha(en lineas: [String]) -> Date? {
        // Primero las líneas con "fecha"/"date" y las dos siguientes (el valor suele ir debajo); después cualquiera
        var ordenadas: [String] = []
        for (i, linea) in lineas.enumerated() where linea.lowercased().contains("fecha") || linea.lowercased().contains("date") {
            ordenadas += lineas[i..<min(i + 3, lineas.count)]
        }
        ordenadas += lineas
        for linea in ordenadas {
            if let f = fechaNumerica(linea) ?? fechaConMes(linea) { return f }
        }
        return nil
    }

    /// 16/04/2026, 16-04-26, 16.04.2026
    private static func fechaNumerica(_ linea: String) -> Date? {
        for m in grupos(#"\b(\d{1,2})[/.\-](\d{1,2})[/.\-](\d{2,4})\b"#, en: linea) {
            if let f = componer(dia: Int(m[0]), mes: Int(m[1]), año: Int(m[2])) { return f }
        }
        return nil
    }

    /// 16 abr 2026, 16 de abril de 2026, 16 sept. 2026, Apr 16, 2026
    private static func fechaConMes(_ linea: String) -> Date? {
        let mes = #"(ene|feb|mar|abr|may|jun|jul|ago|sep|oct|nov|dic|jan|apr|aug|dec)[a-záéíóú]*\.?"#
        for m in grupos("(?i)\\b(\\d{1,2})\\s+(?:de\\s+)?\(mes)\\s+(?:de\\s+)?(\\d{4})\\b", en: linea) {
            if let f = componer(dia: Int(m[0]), mes: meses[m[1].lowercased()], año: Int(m[2])) { return f }
        }
        for m in grupos("(?i)\\b\(mes)\\s+(\\d{1,2}),?\\s+(\\d{4})\\b", en: linea) {
            if let f = componer(dia: Int(m[1]), mes: meses[m[0].lowercased()], año: Int(m[2])) { return f }
        }
        return nil
    }

    private static func componer(dia: Int?, mes: Int?, año: Int?) -> Date? {
        guard let d = dia, let m = mes, var a = año, (1...12).contains(m), (1...31).contains(d) else { return nil }
        if a < 100 { a += 2000 }
        guard (2000...2100).contains(a) else { return nil }
        return Calendar.fiscal.date(from: DateComponents(year: a, month: m, day: d))
    }

    /// Último importe de la primera línea que cumpla `condicion` (o el mayor, si `mayor`).
    /// Si la línea es solo la etiqueta, mira la siguiente.
    private static func importeEnLinea(_ lineas: [String], mayor: Bool = false, _ condicion: (String) -> Bool) -> Decimal? {
        var candidatos: [Decimal] = []
        for (i, linea) in lineas.enumerated() where condicion(etiqueta(linea)) {
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
