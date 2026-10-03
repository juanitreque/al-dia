import Foundation

// MARK: - Numeración

public enum Numeracion {
    /// Serie anual `<prefijo><año>-<NNN>` (2027-001, F2027-001): el contador vuelve a 1 cada año
    /// y sigue al mayor número ya usado en ese año, admita las facturas que admita al mes.
    public static func anual(prefijo: String = "", fecha: Date, existentes: [String], cifras: Int = 3) -> String {
        let año = Calendar.fiscal.component(.year, from: fecha)
        let base = "\(prefijo)\(año)-"
        let ultimo = existentes
            .filter { $0.hasPrefix(base) }
            .compactMap { Int($0.dropFirst(base.count)) }
            .max() ?? 0
        let cifrasTexto = String(ultimo + 1)
        return base + String(repeating: "0", count: max(0, cifras - cifrasTexto.count)) + cifrasTexto
    }

    /// Propone el número de la siguiente factura continuando el formato de la última (ordenadas por fecha).
    /// - `<letras>MMAA` (p. ej. F0926) → mismo prefijo con el mes y año de `fecha`.
    /// - Con el año dentro (2026-087, F2026/087) → si `fecha` es de otro año, ese año y contador a 1.
    /// - Cualquier otro que acabe en dígitos (A099) → incrementa conservando los ceros.
    /// `existentes`: todos los números ya usados, para no repetir ninguno.
    public static func siguiente(despuesDe numeros: [String], fecha: Date, existentes: [String] = []) -> String {
        guard let ultimo = numeros.last(where: { !$0.isEmpty }) else { return "" }
        let usados = Set(numeros + existentes)

        if let m = ultimo.wholeMatch(of: #/(.*?)(20\d{2})([-\/._])(\d+)/#), let añoUltimo = Int(m.2) {
            let año = Calendar.fiscal.component(.year, from: fecha)
            if año != añoUltimo {
                let uno = "1"
                return "\(m.1)\(año)\(m.3)" + String(repeating: "0", count: max(0, m.4.count - 1)) + uno
            }
        }

        if let m = ultimo.wholeMatch(of: #/([A-Za-z]*)(\d{2})(\d{2})/#), let mes = Int(m.2), (1...12).contains(mes) {
            let c = Calendar.fiscal.dateComponents([.year, .month], from: fecha)
            let base = "\(m.1)" + String(format: "%02d%02d", c.month!, c.year! % 100)
            var candidato = base
            var n = 2
            while usados.contains(candidato) {
                candidato = "\(base)-\(n)"
                n += 1
            }
            return candidato
        }

        if let m = ultimo.wholeMatch(of: #/(.*?)(\d+)/#), let valor = Int(m.2) {
            var n = valor + 1
            func formato(_ n: Int) -> String {
                let cifras = String(n)
                return "\(m.1)" + String(repeating: "0", count: max(0, m.2.count - cifras.count)) + cifras
            }
            while usados.contains(formato(n)) { n += 1 }
            return formato(n)
        }
        return ""
    }
}

// MARK: - Lectura de facturas en PDF (plantilla con "Número: … Fecha: …" y tabla CONCEPTO/UNIDAD/PRECIO)

public struct LineaLeida: Sendable, Equatable {
    public var concepto: String
    public var cantidad: Decimal
    public var precio: Decimal
    public var importe: Decimal
}

public struct FacturaLeida: Sendable {
    public var numero: String
    public var fecha: Date
    public var clienteNombre: String
    public var clienteNIF: String
    public var lineas: [LineaLeida]
    public var base: Decimal
    public var tipoIVA: Decimal
    public var cuotaIVA: Decimal
    public var tipoRetencion: Decimal
    public var retencion: Decimal
    public var total: Decimal

    /// Incoherencias detectadas al leer (vacío = todo cuadra).
    public var avisos: [String] {
        var avisos: [String] = []
        if clienteNIF.isEmpty { avisos.append("No se encontró el NIF del cliente") }
        if lineas.isEmpty { avisos.append("No se encontraron líneas de detalle") }
        if !lineas.isEmpty && lineas.suma(\.importe) != base { avisos.append("Las líneas no suman la base") }
        if Calculo.porcentaje(tipoIVA, de: base) != cuotaIVA { avisos.append("La cuota de IVA no corresponde al tipo") }
        if base + cuotaIVA - retencion != total { avisos.append("Base + IVA − retención no da el total") }
        return avisos
    }
}

public enum LectorFactura {
    private static let importe = #"-?(?:\d{1,3}(?:\.\d{3})+|\d+),\d{2}"#
    private static let entero = #"\d+(?:,\d+)?"#

    /// Analiza el texto extraído de un PDF de factura. Devuelve nil si no encuentra número y fecha.
    public static func analizar(_ texto: String) -> FacturaLeida? {
        let lineas = texto.components(separatedBy: .newlines)
            .map { $0.trimmingCharacters(in: .whitespaces) }
            .filter { !$0.isEmpty }

        // "Número: 2026-001 Fecha: 31/01/2026 B12345678"
        guard let cabecera = buscar(lineas, #"N[úu]mero:\s*(\S+)\s+Fecha:\s*(\d{1,2})/(\d{1,2})/(\d{4})(?:\s+([A-Z0-9]{8,10}))?"#),
              let dia = Int(cabecera[1]), let mes = Int(cabecera[2]), let año = Int(cabecera[3]),
              let fecha = Calendar.fiscal.date(from: DateComponents(year: año, month: mes, day: dia))
        else { return nil }

        var cliente = ""
        if let i = lineas.firstIndex(where: { $0.localizedCaseInsensitiveContains("DATOS CLIENTE") }), i + 1 < lineas.count {
            cliente = lineas[i + 1]
        }

        // Detalle entre la cabecera "CONCEPTO …" y "Base …": "PERSONAL(2) 3 35,00 105,00"
        var detalle: [LineaLeida] = []
        if let desde = lineas.firstIndex(where: { $0.uppercased().hasPrefix("CONCEPTO") }) {
            let patron = "^(.+?)\\s+(\(entero))\\s+(\(importe))\\s+(\(importe))$"
            for linea in lineas[(desde + 1)...] {
                if linea.hasPrefix("Base") { break }
                guard let g = grupos(linea, patron) else { continue }
                detalle.append(LineaLeida(concepto: g[0], cantidad: num(g[1]), precio: num(g[2]), importe: num(g[3])))
            }
        }

        // "485,00 21 485,00 15" (base, % IVA, base IRPF, % IRPF)
        let tipos = buscar(lineas, "^(\(importe))\\s+(\(entero))\\s+(\(importe))\\s+(\(entero))$")
        // "485,00 101,85 -72,75 514,10" (base, IVA, −IRPF, líquido)
        let totales = buscar(lineas, "^(\(importe))\\s+(\(importe))\\s+(\(importe))\\s+(\(importe))$")

        let base = totales.map { num($0[0]) } ?? tipos.map { num($0[0]) } ?? detalle.suma(\.importe)
        let tipoIVA = tipos.map { num($0[1]) } ?? 21
        let tipoRetencion = tipos.map { num($0[3]) } ?? 0
        let cuota = totales.map { num($0[1]) } ?? Calculo.porcentaje(tipoIVA, de: base)
        let retencion = totales.map { abs(num($0[2])) } ?? Calculo.porcentaje(tipoRetencion, de: base)
        let total = totales.map { num($0[3]) } ?? base + cuota - retencion

        return FacturaLeida(numero: cabecera[0], fecha: fecha, clienteNombre: cliente, clienteNIF: cabecera[4],
                            lineas: detalle, base: base, tipoIVA: tipoIVA, cuotaIVA: cuota,
                            tipoRetencion: tipoRetencion, retencion: retencion, total: total)
    }

    private static func num(_ texto: String) -> Decimal { Importe.parse(texto) ?? 0 }

    private static func buscar(_ lineas: [String], _ patron: String) -> [String]? {
        for linea in lineas {
            if let g = grupos(linea, patron) { return g }
        }
        return nil
    }

    /// Grupos de captura de la primera coincidencia ("" para los que no participan).
    private static func grupos(_ texto: String, _ patron: String) -> [String]? {
        guard let regex = try? NSRegularExpression(pattern: patron),
              let m = regex.firstMatch(in: texto, range: NSRange(texto.startIndex..., in: texto))
        else { return nil }
        return (1..<m.numberOfRanges).map { i in
            Range(m.range(at: i), in: texto).map { String(texto[$0]) } ?? ""
        }
    }
}

// MARK: - Ficheros de importación de la aplicación gratuita VERI*FACTU de la AEAT

/// Formatos de texto plano separados por "#" que acepta la app de la AEAT en
/// Otros servicios → Clientes / Productos → Importar.
public enum ExportacionAEAT {
    public struct Cliente: Sendable {
        public var nombre, nif, direccion, codigoPostal, poblacion, provincia, pais, email, telefono: String
        public init(nombre: String, nif: String, direccion: String = "", codigoPostal: String = "", poblacion: String = "",
                    provincia: String = "", pais: String = "ES", email: String = "", telefono: String = "") {
            self.nombre = nombre
            self.nif = nif
            self.direccion = direccion
            self.codigoPostal = codigoPostal
            self.poblacion = poblacion
            self.provincia = provincia
            self.pais = pais
            self.email = email
            self.telefono = telefono
        }
    }

    public struct Producto: Sendable {
        public var id, descripcion: String
        public var precio, tipoIVA: Decimal
        public init(id: String, descripcion: String, precio: Decimal, tipoIVA: Decimal) {
            self.id = id
            self.descripcion = descripcion
            self.precio = precio
            self.tipoIVA = tipoIVA
        }
    }

    public static let cabeceraClientes =
        "Nombre o Razón Social#NIF#Dirección#Código Postal#Población#Provincia#Símbolo País#Email#Teléfono#Web"
    public static let cabeceraProductos =
        "ID#Descripción#Precio Unitario#Concepto Descuento#Importe Descuento#Código IVA/IPSI/IGIC#Código Clave Régimen#Código de Calificación Operación u Operación Exenta#Tipo Impositivo#Tipo Recargo de Equivalencia"

    public static func clientes(_ clientes: [Cliente]) -> String {
        let filas = clientes.map { c in
            [c.nombre, c.nif, c.direccion, c.codigoPostal, c.poblacion, c.provincia, c.pais, c.email, c.telefono, ""]
                .map(campo).joined(separator: "#")
        }
        return ([cabeceraClientes] + filas).joined(separator: "\r\n") + "\r\n"
    }

    /// Productos sujetos a IVA en régimen general: impuesto 01 (IVA), clave de régimen 01 (general),
    /// calificación S1 (sujeta y no exenta). Precio con punto decimal.
    public static func productos(_ productos: [Producto]) -> String {
        let filas = productos.map { p in
            let sujeto = p.tipoIVA > 0
            return [identificador(p.id), p.descripcion, decimal(p.precio), "", "",
                    sujeto ? "01" : "", sujeto ? "01" : "", sujeto ? "S1" : "", sujeto ? decimal(p.tipoIVA) : "", ""]
                .map(campo).joined(separator: "#")
        }
        return ([cabeceraProductos] + filas).joined(separator: "\r\n") + "\r\n"
    }

    /// El ID de producto no admite espacios.
    public static func identificador(_ texto: String) -> String {
        texto.trimmingCharacters(in: .whitespaces).replacingOccurrences(of: " ", with: "_")
    }

    static func campo(_ texto: String) -> String {
        texto.replacingOccurrences(of: "#", with: " ")
            .components(separatedBy: .newlines).joined(separator: " ")
            .trimmingCharacters(in: .whitespaces)
    }

    static func decimal(_ valor: Decimal) -> String {
        valor.formatted(.number.precision(.fractionLength(2)).grouping(.never).locale(Locale(identifier: "en_US_POSIX")))
    }
}
