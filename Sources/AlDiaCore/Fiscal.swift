import Foundation

// MARK: - Utilidades numéricas

public extension Decimal {
    /// Redondeo comercial (half-up) a `escala` decimales.
    func redondeado(_ escala: Int = 2) -> Decimal {
        var origen = self
        var resultado = Decimal()
        NSDecimalRound(&resultado, &origen, escala, .plain)
        return resultado
    }
}

public extension Sequence {
    func suma(_ campo: KeyPath<Element, Decimal>) -> Decimal {
        reduce(0) { $0 + $1[keyPath: campo] }
    }
}

public enum Calculo {
    /// `tipo` % de `base`, redondeado a céntimos.
    public static func porcentaje(_ tipo: Decimal, de base: Decimal) -> Decimal {
        (base * tipo / 100).redondeado()
    }
}

/// Conversión entre importes y texto en formato español ("1234,56").
public enum Importe {
    static let es = Locale(identifier: "es_ES")

    /// Acepta "45,50", "1.234,56", "45.5", "45 €". Devuelve nil si está vacío o no es un número.
    public static func parse(_ texto: String) -> Decimal? {
        var t = texto
            .replacingOccurrences(of: "€", with: "")
            .replacingOccurrences(of: " ", with: "")
            .replacingOccurrences(of: "\u{00A0}", with: "")
        if t.contains(",") {
            t = t.replacingOccurrences(of: ".", with: "").replacingOccurrences(of: ",", with: ".")
        }
        guard !t.isEmpty, t.range(of: #"^-?\d+(\.\d+)?\.?$"#, options: .regularExpression) != nil else { return nil }
        return Decimal(string: t, locale: Locale(identifier: "en_US_POSIX"))
    }

    /// Texto editable sin separador de miles: 1234,5 → "1234,50". El cero se muestra vacío.
    public static func texto(_ valor: Decimal, decimales: ClosedRange<Int> = 2...2) -> String {
        guard valor != 0 else { return "" }
        return valor.formatted(.number.precision(.fractionLength(decimales)).grouping(.never).locale(es))
    }
}

// MARK: - Calendario fiscal

public extension Calendar {
    static let fiscal: Calendar = {
        var c = Calendar(identifier: .gregorian)
        c.timeZone = TimeZone(identifier: "Europe/Madrid")!
        c.locale = Locale(identifier: "es_ES")
        c.firstWeekday = 2
        return c
    }()
}

public struct Trimestre: Hashable, Comparable, Identifiable, Sendable {
    public let ejercicio: Int
    public let numero: Int

    public init(ejercicio: Int, numero: Int) {
        precondition((1...4).contains(numero), "Trimestre fuera de rango")
        self.ejercicio = ejercicio
        self.numero = numero
    }

    public static func de(_ fecha: Date) -> Trimestre {
        let c = Calendar.fiscal.dateComponents([.year, .month], from: fecha)
        return Trimestre(ejercicio: c.year!, numero: (c.month! - 1) / 3 + 1)
    }

    /// Trimestre cuya declaración toca presentar a fecha `hoy` (el anterior al actual).
    public static func aPresentar(hoy: Date) -> Trimestre { de(hoy).anterior }

    public var id: String { "\(ejercicio)-\(numero)" }
    public var periodo: String { "\(numero)T" }
    public var etiqueta: String { "\(numero)T \(ejercicio)" }

    public var inicio: Date {
        Calendar.fiscal.date(from: DateComponents(year: ejercicio, month: (numero - 1) * 3 + 1, day: 1))!
    }
    /// Límite exclusivo (primer instante del trimestre siguiente).
    public var fin: Date { Calendar.fiscal.date(byAdding: .month, value: 3, to: inicio)! }

    public func contiene(_ fecha: Date) -> Bool { fecha >= inicio && fecha < fin }

    public var anterior: Trimestre {
        numero == 1 ? Trimestre(ejercicio: ejercicio - 1, numero: 4) : Trimestre(ejercicio: ejercicio, numero: numero - 1)
    }
    public var siguiente: Trimestre {
        numero == 4 ? Trimestre(ejercicio: ejercicio + 1, numero: 1) : Trimestre(ejercicio: ejercicio, numero: numero + 1)
    }

    /// Último día para presentar 303/130: día 20 del mes siguiente (30 de enero para el 4T).
    /// Si cae en fin de semana pasa al lunes. No contempla festivos.
    public var plazoPresentacion: Date {
        let componentes = numero == 4
            ? DateComponents(year: ejercicio + 1, month: 1, day: 30)
            : DateComponents(year: ejercicio, month: numero * 3 + 1, day: 20)
        var dia = Calendar.fiscal.date(from: componentes)!
        while Calendar.fiscal.isDateInWeekend(dia) {
            dia = Calendar.fiscal.date(byAdding: .day, value: 1, to: dia)!
        }
        return dia
    }

    public static func < (a: Trimestre, b: Trimestre) -> Bool {
        (a.ejercicio, a.numero) < (b.ejercicio, b.numero)
    }
}

/// Filtro de periodo para listados: un ejercicio completo o uno de sus trimestres.
public struct Periodo: Hashable, Sendable {
    public var ejercicio: Int
    public var trimestre: Int?

    public init(ejercicio: Int, trimestre: Int? = nil) {
        self.ejercicio = ejercicio
        self.trimestre = trimestre
    }

    public func contiene(_ fecha: Date) -> Bool {
        if let trimestre {
            return Trimestre(ejercicio: ejercicio, numero: trimestre).contiene(fecha)
        }
        return Calendar.fiscal.component(.year, from: fecha) == ejercicio
    }

    public var etiqueta: String {
        trimestre.map { "\($0)T \(ejercicio)" } ?? String(ejercicio)
    }
}

// MARK: - Datos de entrada (independientes de la persistencia)

public struct DatosIngreso: Sendable {
    public var fecha: Date
    public var base: Decimal
    public var tipoIVA: Decimal
    public var cuotaIVA: Decimal
    public var retencion: Decimal

    public init(fecha: Date, base: Decimal, tipoIVA: Decimal, cuotaIVA: Decimal, retencion: Decimal) {
        self.fecha = fecha
        self.base = base
        self.tipoIVA = tipoIVA
        self.cuotaIVA = cuotaIVA
        self.retencion = retencion
    }
}

public struct DatosGasto: Sendable {
    public var fecha: Date
    public var base: Decimal
    public var cuotaIVA: Decimal
    /// % de la cuota que es deducible (100 general, 50 vehículo de uso mixto…).
    public var porcentajeDeducibleIVA: Decimal
    /// Sin factura completa (con tu NIF) el IVA no es deducible.
    public var facturaCompleta: Bool
    public var deducibleIRPF: Bool
    public var bienInversion: Bool

    public init(fecha: Date, base: Decimal, cuotaIVA: Decimal, porcentajeDeducibleIVA: Decimal = 100,
                facturaCompleta: Bool = true, deducibleIRPF: Bool = true, bienInversion: Bool = false) {
        self.fecha = fecha
        self.base = base
        self.cuotaIVA = cuotaIVA
        self.porcentajeDeducibleIVA = porcentajeDeducibleIVA
        self.facturaCompleta = facturaCompleta
        self.deducibleIRPF = deducibleIRPF
        self.bienInversion = bienInversion
    }

    public var ivaDeducible: Decimal {
        guard facturaCompleta else { return 0 }
        return Calculo.porcentaje(porcentajeDeducibleIVA, de: cuotaIVA)
    }

    /// Base que se declara en el 303 junto a la cuota deducible.
    public var baseDeducible: Decimal {
        guard ivaDeducible > 0 else { return 0 }
        return Calculo.porcentaje(porcentajeDeducibleIVA, de: base)
    }

    /// Gasto computable en IRPF: base + IVA no deducible. Los bienes de inversión se amortizan, no se imputan aquí.
    public var gastoIRPF: Decimal {
        guard deducibleIRPF, !bienInversion else { return 0 }
        return base + cuotaIVA - ivaDeducible
    }
}

// MARK: - Modelo 303 (IVA trimestral, régimen general)

public struct Liquidacion303: Sendable {
    public struct Devengo: Sendable, Identifiable {
        public let tipo: Decimal
        public let base: Decimal
        public let cuota: Decimal
        public var id: Decimal { tipo }

        /// Casillas del 303 (base, cuota) para el tipo.
        public var casillas: (base: String, cuota: String)? {
            switch tipo {
            case 4: ("01", "03")
            case 10: ("04", "06")
            case 21: ("07", "09")
            default: nil
            }
        }
    }

    public let trimestre: Trimestre
    public let devengos: [Devengo]
    public let baseCorrientes: Decimal   // 28
    public let cuotaCorrientes: Decimal  // 29
    public let baseInversion: Decimal    // 30
    public let cuotaInversion: Decimal   // 31

    public init(trimestre: Trimestre, ingresos: [DatosIngreso], gastos: [DatosGasto]) {
        self.trimestre = trimestre
        let ingresosTrimestre = ingresos.filter { trimestre.contiene($0.fecha) && $0.tipoIVA > 0 }
        devengos = Dictionary(grouping: ingresosTrimestre, by: \.tipoIVA)
            .map { tipo, lineas in Devengo(tipo: tipo, base: lineas.suma(\.base), cuota: lineas.suma(\.cuotaIVA)) }
            .sorted { $0.tipo < $1.tipo }

        let deducibles = gastos.filter { trimestre.contiene($0.fecha) && $0.ivaDeducible > 0 }
        let corrientes = deducibles.filter { !$0.bienInversion }
        let inversion = deducibles.filter(\.bienInversion)
        baseCorrientes = corrientes.suma(\.baseDeducible)
        cuotaCorrientes = corrientes.suma(\.ivaDeducible)
        baseInversion = inversion.suma(\.baseDeducible)
        cuotaInversion = inversion.suma(\.ivaDeducible)
    }

    /// Casilla 27.
    public var totalDevengado: Decimal { devengos.suma(\.cuota) }
    /// Casilla 45.
    public var totalDeducir: Decimal { cuotaCorrientes + cuotaInversion }
    /// Casilla 46 (= 69 para un autónomo sin otras regularizaciones).
    public var resultado: Decimal { totalDevengado - totalDeducir }

    /// Resultado tras aplicar cuotas pendientes de compensar de periodos anteriores.
    /// Negativo = sigue quedando a compensar (o a devolver si es 4T).
    public func resultado(compensando pendiente: Decimal) -> Decimal { resultado - pendiente }
}

// MARK: - Modelo 130 (pago fraccionado IRPF, estimación directa)

public struct Liquidacion130: Sendable {
    public let trimestre: Trimestre
    public let ingresos: Decimal        // 01 acumulado desde enero
    public let gastos: Decimal          // 02 acumulado desde enero
    public let pagosAnteriores: Decimal // 05
    public let retenciones: Decimal     // 06 acumulado desde enero

    public init(trimestre: Trimestre, ingresos: [DatosIngreso], gastos: [DatosGasto], pagosAnteriores: Decimal) {
        self.trimestre = trimestre
        let desde = Trimestre(ejercicio: trimestre.ejercicio, numero: 1).inicio
        let acumulado: (Date) -> Bool = { $0 >= desde && $0 < trimestre.fin }
        let ingresosAcumulados = ingresos.filter { acumulado($0.fecha) }
        self.ingresos = ingresosAcumulados.suma(\.base)
        self.retenciones = ingresosAcumulados.suma(\.retencion)
        self.gastos = gastos.filter { acumulado($0.fecha) }.suma(\.gastoIRPF)
        self.pagosAnteriores = pagosAnteriores
    }

    /// Casilla 03.
    public var rendimiento: Decimal { ingresos - gastos }
    /// Casilla 04: 20 % del rendimiento positivo.
    public var cuota: Decimal { max(0, Calculo.porcentaje(20, de: rendimiento)) }
    /// Casilla 07. Si es ≤ 0 la declaración sale negativa (0 a ingresar).
    public var resultado: Decimal { cuota - pagosAnteriores - retenciones }

    /// % de ingresos del ejercicio sujetos a retención. Si en el año anterior fue ≥ 70 %,
    /// no hay obligación de presentar el 130 (art. 109.1 RIRPF).
    public static func porcentajeConRetencion(_ ingresos: [DatosIngreso], ejercicio: Int) -> Decimal? {
        let delAño = ingresos.filter { Calendar.fiscal.component(.year, from: $0.fecha) == ejercicio }
        let total = delAño.suma(\.base)
        guard total > 0 else { return nil }
        return (delAño.filter { $0.retencion > 0 }.suma(\.base) * 100 / total).redondeado(1)
    }
}
