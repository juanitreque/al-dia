import Foundation
import SwiftData
import AlDiaCore

/// Modo demostración (`--demo`): base de datos temporal con datos ficticios de un autónomo
/// con varios clientes, para probar la app y hacer capturas sin tocar datos reales.
enum Demo {
    static var activo: Bool { CommandLine.arguments.contains("--demo") }

    static var carpeta: URL {
        FileManager.default.temporaryDirectory.appendingPathComponent("AlDiaDemo", isDirectory: true)
    }

    @MainActor
    static func rellenar(_ contenedor: ModelContainer) {
        let c = contenedor.mainContext
        let cal = Calendar.fiscal
        func dia(_ mes: Int, _ d: Int) -> Date { cal.date(from: DateComponents(year: 2026, month: mes, day: d))! }
        func finDeMes(_ mes: Int) -> Date { cal.date(byAdding: DateComponents(month: 1, day: -1), to: dia(mes, 1))! }

        func cliente(_ nombre: String, _ nif: String, _ cp: String, _ poblacion: String) -> Cliente {
            let nuevo = Cliente(nombre: nombre)
            nuevo.nif = nif
            nuevo.direccion = "C/ Mayor, 1"
            nuevo.codigoPostal = cp
            nuevo.poblacion = poblacion
            nuevo.provincia = poblacion
            c.insert(nuevo)
            return nuevo
        }
        let estudio = cliente("Estudio Norte, SL", "B00000001", "28001", "Madrid")
        let cafeteria = cliente(String(localized: "Cafetería La Plaza, SL"), "B00000002", "46001", "Valencia")
        let asesoria = cliente(String(localized: "Asesoría Lumen, SL"), "B00000003", "41001", "Sevilla")
        let talleres = cliente("Talleres Ruiz, SL", "B00000004", "48001", "Bilbao")

        let consultoria = Servicio(codigo: "CONSULTORIA", descripcion: String(localized: "Hora de consultoría"), precio: 40)
        let web = Servicio(codigo: "WEB", descripcion: String(localized: "Diseño de página web"), precio: 650)
        let mantenimiento = Servicio(codigo: "MANTENIMIENTO", descripcion: String(localized: "Mantenimiento web mensual"), precio: 90)
        [consultoria, web, mantenimiento].forEach(c.insert)

        var contador = 0
        func factura(_ fecha: Date, _ cliente: Cliente, _ lineas: [(Servicio, Decimal)],
                     estado: EstadoFactura = .emitida, cobrada: Bool = true) {
            contador += 1
            let i = Ingreso()
            c.insert(i)
            i.estado = estado
            i.numero = String(format: "2026-%03d", contador)
            i.fecha = fecha
            i.cliente = cliente
            i.concepto = conceptoSugerido(fecha)
            i.lineas = lineas.enumerated().map { n, l in
                LineaIngreso(orden: n, codigo: l.0.codigo, concepto: l.0.descripcion, cantidad: l.1, precio: l.0.precio)
            }
            i.base = lineas.map { ($0.1 * $0.0.precio).redondeado() }.reduce(0, +)
            i.cuotaIVA = Calculo.porcentaje(21, de: i.base)
            i.retencion = Calculo.porcentaje(15, de: i.base)
            i.cobrada = estado == .emitida && cobrada
            i.fechaCobro = i.cobrada ? cal.date(byAdding: .day, value: 15, to: fecha) : nil
            if estado == .emitida { i.adjuntoNombre = "Factura \(i.numero).pdf"; i.adjunto = Data() }
        }

        let horas: [Decimal] = [12, 9, 15, 10, 14, 8, 6, 11, 13]
        for mes in 1...9 {
            let pagada = mes < 9
            factura(dia(mes, 10), estudio, [(consultoria, horas[mes - 1])], cobrada: pagada)
            for cliente in [cafeteria, asesoria, talleres] {
                factura(finDeMes(mes), cliente, [(mantenimiento, 1)], cobrada: pagada)
            }
            if mes == 3 { factura(dia(mes, 20), cafeteria, [(web, 1)]) }
            if mes == 6 { factura(dia(mes, 20), talleres, [(web, 1)]) }
        }
        factura(dia(10, 10), estudio, [(consultoria, 12)], estado: .borrador)
        factura(finDeMes(10), cafeteria, [(mantenimiento, 1)], estado: .borrador)

        func gasto(_ fecha: Date, _ proveedor: String, _ concepto: String, _ categoria: CategoriaGasto,
                   _ base: Decimal, iva: Decimal = 21) {
            let g = Gasto()
            c.insert(g)
            g.fecha = fecha
            g.proveedor = proveedor
            g.concepto = concepto
            g.categoria = categoria
            g.base = base
            g.tipoIVA = iva
            g.cuotaIVA = Calculo.porcentaje(iva, de: base)
            g.numeroFactura = String(format: "F-%02d%02d", cal.component(.month, from: fecha), cal.component(.day, from: fecha))
            g.adjuntoNombre = "\(proveedor).pdf"
            g.adjunto = Data()
        }
        for mes in 1...9 {
            gasto(dia(mes, 1), String(localized: "Proveedor de software"), String(localized: "Suscripción de software"), .software, Decimal(string: "24.19")!)
            gasto(dia(mes, 5), String(localized: "Espacio de coworking"), String(localized: "Puesto fijo mensual"), .otros, 150)
            gasto(dia(mes, 28), String(localized: "Operador móvil"), String(localized: "Línea móvil"), .telefono, Decimal(string: "24.79")!)
            gasto(dia(mes, 30), String(localized: "Seguridad Social"), String(localized: "Cuota de autónomos"), .cuotaAutonomo, Decimal(string: "294.00")!, iva: 0)
        }
        for mes in [3, 6, 9] {
            gasto(dia(mes, 25), String(localized: "Gestoría Ejemplo"), String(localized: "Asesoría fiscal trimestral"), .gestoria, 60)
        }
        gasto(dia(4, 20), String(localized: "Escuela online"), String(localized: "Curso de formación"), .formacion, 120)
        gasto(dia(9, 21), String(localized: "Papelería Ejemplo"), String(localized: "Tóner y papel"), .material, Decimal(string: "49.59")!)

        let ingresos = (try? c.fetch(FetchDescriptor<Ingreso>()))?.emitidas.map(\.datos) ?? []
        let gastos = (try? c.fetch(FetchDescriptor<Gasto>()))?.map(\.datos) ?? []
        for t in 1...2 {
            let trimestre = Trimestre(ejercicio: 2026, numero: t)
            let m = ModeloPresentado()
            c.insert(m)
            m.modelo = TipoModelo.m303.rawValue
            m.ejercicio = 2026
            m.periodo = trimestre.periodo
            m.importe = Liquidacion303(trimestre: trimestre, ingresos: ingresos, gastos: gastos).resultado
            m.fechaPresentacion = cal.date(byAdding: .day, value: -3, to: trimestre.plazoPresentacion)!
            m.justificante = "NRC 0000000000\(t)"
        }
        try? c.save()
    }
}
