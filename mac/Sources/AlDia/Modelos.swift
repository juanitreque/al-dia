import Foundation
import SwiftData
import AlDiaCore

@Model final class Cliente {
    var nombre: String = ""
    var nif: String = ""
    var direccion: String = ""
    var codigoPostal: String = ""
    var poblacion: String = ""
    var provincia: String = ""
    var pais: String = "ES"
    var email: String = ""
    var telefono: String = ""
    var notas: String = ""
    @Relationship(deleteRule: .nullify, inverse: \Ingreso.cliente) var ingresos: [Ingreso]? = []

    init(nombre: String = "") {
        self.nombre = nombre
    }

    var paraAEAT: ExportacionAEAT.Cliente {
        ExportacionAEAT.Cliente(nombre: nombre, nif: nif, direccion: direccion, codigoPostal: codigoPostal,
                                poblacion: poblacion, provincia: provincia, pais: pais, email: email, telefono: telefono)
    }
}

/// Producto o servicio del catálogo (se exporta a la app de la AEAT).
@Model final class Servicio {
    var codigo: String = ""
    var descripcion: String = ""
    var precio: Decimal = 0
    var tipoIVA: Decimal = 21

    init(codigo: String = "", descripcion: String = "", precio: Decimal = 0, tipoIVA: Decimal = 21) {
        self.codigo = codigo
        self.descripcion = descripcion
        self.precio = precio
        self.tipoIVA = tipoIVA
    }

    var paraAEAT: ExportacionAEAT.Producto {
        ExportacionAEAT.Producto(id: codigo, descripcion: descripcion, precio: precio, tipoIVA: tipoIVA)
    }
}

enum EstadoFactura: String, CaseIterable, Identifiable {
    /// Preparada en Al Día, pendiente de emitir en la app de la AEAT. No cuenta para impuestos.
    case borrador = "Borrador"
    /// Emitida oficialmente (AEAT, gestor o anterior a VeriFactu).
    case emitida = "Emitida"

    var id: String { rawValue }

    /// Nombre visible (el `rawValue` se guarda en la base de datos y no se traduce).
    var nombre: String {
        switch self {
        case .borrador: String(localized: "Borrador")
        case .emitida: String(localized: "Emitida")
        }
    }
}

/// Factura de venta: borrador preparado aquí o factura ya emitida oficialmente.
@Model final class Ingreso {
    var estadoRaw: String = EstadoFactura.emitida.rawValue
    var numero: String = ""
    var fecha: Date = Date()
    var cliente: Cliente?
    var concepto: String = ""
    var base: Decimal = 0
    var tipoIVA: Decimal = 21
    var cuotaIVA: Decimal = 0
    var tipoRetencion: Decimal = 15
    var retencion: Decimal = 0
    var cobrada: Bool = false
    var fechaCobro: Date?
    var notas: String = ""
    @Attribute(.externalStorage) var adjunto: Data?
    var adjuntoNombre: String?
    @Relationship(deleteRule: .cascade, inverse: \LineaIngreso.ingreso) var lineas: [LineaIngreso]? = []

    init() {}

    var estado: EstadoFactura {
        get { EstadoFactura(rawValue: estadoRaw) ?? .emitida }
        set { estadoRaw = newValue.rawValue }
    }

    var esBorrador: Bool { estado == .borrador }

    var lineasOrdenadas: [LineaIngreso] { (lineas ?? []).sorted { $0.orden < $1.orden } }

    var total: Decimal { base + cuotaIVA - retencion }

    var datos: DatosIngreso {
        DatosIngreso(fecha: fecha, base: base, tipoIVA: tipoIVA, cuotaIVA: cuotaIVA, retencion: retencion)
    }
}

@Model final class LineaIngreso {
    var orden: Int = 0
    var codigo: String = ""
    var concepto: String = ""
    var cantidad: Decimal = 1
    var precio: Decimal = 0
    var ingreso: Ingreso?

    init(orden: Int, codigo: String, concepto: String, cantidad: Decimal, precio: Decimal) {
        self.orden = orden
        self.codigo = codigo
        self.concepto = concepto
        self.cantidad = cantidad
        self.precio = precio
    }

    var importe: Decimal { (cantidad * precio).redondeado() }
}

extension Array where Element == Ingreso {
    /// Solo las facturas emitidas cuentan para impuestos y totales.
    var emitidas: [Ingreso] { filter { !$0.esBorrador } }
}

enum CategoriaGasto: String, CaseIterable, Identifiable {
    /// El valor guardado conserva el nombre original; se muestra como «Material y equipamiento».
    case material = "Material deportivo"
    case formacion = "Formación y certificaciones"
    case vehiculo = "Vehículo y desplazamientos"
    case telefono = "Teléfono e internet"
    case cuotaAutonomo = "Cuota de autónomos (RETA)"
    case seguros = "Seguros"
    case gestoria = "Gestoría y asesoría"
    case software = "Software y suscripciones"
    case banco = "Comisiones bancarias"
    case otros = "Otros"

    var id: String { rawValue }

    var nombre: String {
        switch self {
        case .material: String(localized: "Material y equipamiento")
        case .formacion: String(localized: "Formación y certificaciones")
        case .vehiculo: String(localized: "Vehículo y desplazamientos")
        case .telefono: String(localized: "Teléfono e internet")
        case .cuotaAutonomo: String(localized: "Cuota de autónomos (RETA)")
        case .seguros: String(localized: "Seguros")
        case .gestoria: String(localized: "Gestoría y asesoría")
        case .software: String(localized: "Software y suscripciones")
        case .banco: String(localized: "Comisiones bancarias")
        case .otros: String(localized: "Otros")
        }
    }

    /// Valores habituales al elegir la categoría en un gasto nuevo.
    var sugerencia: (tipoIVA: Decimal, porcentajeDeducible: Decimal)? {
        switch self {
        case .cuotaAutonomo, .seguros, .banco: (0, 100)
        case .vehiculo: (21, 50)
        default: nil
        }
    }
}

/// Factura recibida / gasto de la actividad.
@Model final class Gasto {
    var fecha: Date = Date()
    var proveedor: String = ""
    var nifProveedor: String = ""
    var numeroFactura: String = ""
    var concepto: String = ""
    var categoriaRaw: String = CategoriaGasto.otros.rawValue
    var base: Decimal = 0
    var tipoIVA: Decimal = 21
    var cuotaIVA: Decimal = 0
    var porcentajeDeducibleIVA: Decimal = 100
    var facturaCompleta: Bool = true
    var deducibleIRPF: Bool = true
    var bienInversion: Bool = false
    var notas: String = ""
    @Attribute(.externalStorage) var adjunto: Data?
    var adjuntoNombre: String?

    init() {}

    var categoria: CategoriaGasto {
        get { CategoriaGasto(rawValue: categoriaRaw) ?? .otros }
        set { categoriaRaw = newValue.rawValue }
    }

    var total: Decimal { base + cuotaIVA }

    var datos: DatosGasto {
        DatosGasto(fecha: fecha, base: base, cuotaIVA: cuotaIVA, porcentajeDeducibleIVA: porcentajeDeducibleIVA,
                   facturaCompleta: facturaCompleta, deducibleIRPF: deducibleIRPF, bienInversion: bienInversion)
    }
}

enum TipoModelo: String, CaseIterable, Identifiable {
    case m303 = "303"
    case m130 = "130"
    case m390 = "390"
    case m347 = "347"
    case m100 = "100"
    case otro = "Otro"

    var id: String { rawValue }

    var descripcion: String {
        switch self {
        case .m303: String(localized: "303 · IVA trimestral")
        case .m130: String(localized: "130 · Pago fraccionado IRPF")
        case .m390: String(localized: "390 · Resumen anual IVA")
        case .m347: String(localized: "347 · Operaciones con terceros")
        case .m100: String(localized: "100 · Renta")
        case .otro: String(localized: "Otro")
        }
    }
}

enum TipoResultado: String, CaseIterable, Identifiable {
    case ingresar = "A ingresar"
    case compensar = "A compensar"
    case devolver = "A devolver"
    case negativa = "Negativa / cero"

    var id: String { rawValue }

    var nombre: String {
        switch self {
        case .ingresar: String(localized: "A ingresar")
        case .compensar: String(localized: "A compensar")
        case .devolver: String(localized: "A devolver")
        case .negativa: String(localized: "Negativa / cero")
        }
    }
}

/// Declaración presentada en la sede de la AEAT.
@Model final class ModeloPresentado {
    var modelo: String = TipoModelo.m303.rawValue
    var ejercicio: Int = 2026
    /// "1T"…"4T" o "0A" para anuales.
    var periodo: String = "1T"
    /// Importe siempre positivo; el sentido lo da `tipoResultadoRaw`.
    var importe: Decimal = 0
    var tipoResultadoRaw: String = TipoResultado.ingresar.rawValue
    var fechaPresentacion: Date = Date()
    /// NRC del pago o CSV del justificante.
    var justificante: String = ""
    var notas: String = ""
    @Attribute(.externalStorage) var adjunto: Data?
    var adjuntoNombre: String?

    init() {}

    var tipoResultado: TipoResultado {
        get { TipoResultado(rawValue: tipoResultadoRaw) ?? .ingresar }
        set { tipoResultadoRaw = newValue.rawValue }
    }

    var tipo: TipoModelo { TipoModelo(rawValue: modelo) ?? .otro }

    func corresponde(a tipo: TipoModelo, _ trimestre: Trimestre) -> Bool {
        modelo == tipo.rawValue && ejercicio == trimestre.ejercicio && periodo == trimestre.periodo
    }
}

// MARK: - Persistencia

enum Almacen {
    private static var soporte: URL {
        FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
    }

    static var carpeta: URL { soporte.appendingPathComponent("AlDia", isDirectory: true) }

    static func contenedor() throws -> ModelContainer {
        let carpeta = Demo.activo ? Demo.carpeta : carpeta
        if Demo.activo {
            try? FileManager.default.removeItem(at: carpeta)
        } else {
            migrarDesdeCuentas()
        }
        try FileManager.default.createDirectory(at: carpeta, withIntermediateDirectories: true)
        let configuracion = ModelConfiguration(url: carpeta.appendingPathComponent("AlDia.store"))
        return try ModelContainer(
            for: Cliente.self, Servicio.self, Ingreso.self, LineaIngreso.self, Gasto.self, ModeloPresentado.self,
            configurations: configuracion
        )
    }

    /// La app se llamaba "Cuentas" (es.juanitreque.cuentas). La primera vez que se abre como Al Día,
    /// mueve la base de datos (store, -wal, -shm y adjuntos externos) y los ajustes a los nombres nuevos.
    private static func migrarDesdeCuentas() {
        let fm = FileManager.default
        let antigua = soporte.appendingPathComponent("Cuentas", isDirectory: true)
        guard fm.fileExists(atPath: antigua.appendingPathComponent("Cuentas.store").path),
              !fm.fileExists(atPath: carpeta.path)
        else { return }
        do {
            try fm.moveItem(at: antigua, to: carpeta)
            for sufijo in ["", "-wal", "-shm"] {
                let origen = carpeta.appendingPathComponent("Cuentas.store" + sufijo)
                if fm.fileExists(atPath: origen.path) {
                    try fm.moveItem(at: origen, to: carpeta.appendingPathComponent("AlDia.store" + sufijo))
                }
            }
            let adjuntos = carpeta.appendingPathComponent(".Cuentas_SUPPORT")
            if fm.fileExists(atPath: adjuntos.path) {
                try fm.moveItem(at: adjuntos, to: carpeta.appendingPathComponent(".AlDia_SUPPORT"))
            }
        } catch {
            NSLog("Migración desde Cuentas incompleta: \(error)")
        }

        if let anteriores = UserDefaults.standard.persistentDomain(forName: "es.juanitreque.cuentas") {
            for clave in [Ajustes.nombre, Ajustes.nif, Ajustes.ivaDefecto, Ajustes.retencionDefecto]
            where UserDefaults.standard.object(forKey: clave) == nil {
                UserDefaults.standard.set(anteriores[clave], forKey: clave)
            }
        }
    }
}
