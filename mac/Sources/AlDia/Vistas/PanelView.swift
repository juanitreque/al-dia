import SwiftData
import SwiftUI
import AlDiaCore

struct PanelView: View {
    let irA: (Seccion) -> Void

    @Query private var todosLosIngresos: [Ingreso]
    private var ingresos: [Ingreso] { todosLosIngresos.emitidas }
    @Query private var gastos: [Gasto]
    @Query private var modelos: [ModeloPresentado]
    @AppStorage(Ajustes.nombre) private var nombre = ""

    private let hoy = Date()

    var body: some View {
        let datosIngresos = ingresos.map(\.datos)
        let datosGastos = gastos.map(\.datos)
        let enCurso = Trimestre.de(hoy)
        let aPresentar = Trimestre.aPresentar(hoy: hoy)
        let ejercicio = enCurso.ejercicio
        let liquidacion = Liquidacion303(trimestre: enCurso, ingresos: datosIngresos, gastos: datosGastos)
        let delAño = Periodo(ejercicio: ejercicio)
        let ingresosAño = ingresos.filter { delAño.contiene($0.fecha) }
        let gastosAño = datosGastos.filter { delAño.contiene($0.fecha) }
        let pendientes = ingresos.filter { !$0.cobrada }

        ScrollView {
            VStack(alignment: .leading, spacing: 28) {
                VStack(alignment: .leading, spacing: 4) {
                    Text(nombre.isEmpty ? "Al Día" : nombre).font(.largeTitle.bold())
                    Text(hoy.formatted(.dateTime.weekday(.wide).day().month(.wide).year()).capitalizedFirst)
                        .foregroundStyle(.secondary)
                }

                TarjetaPlazo(trimestre: aPresentar, hoy: hoy, modelos: modelos,
                             ingresos: datosIngresos, irAModelos: { irA(.modelos) })

                let borradores = todosLosIngresos.filter(\.esBorrador)
                if !borradores.isEmpty {
                    HStack {
                        Label("\(borradores.count) factura(s) en borrador pendientes de emitir en la AEAT: \(borradores.map(\.numero).joined(separator: ", "))",
                              systemImage: "doc.badge.clock")
                        Spacer()
                        Button("Ir a ingresos") { irA(.ingresos) }
                    }
                    .padding(14)
                    .background(.orange.opacity(0.12), in: RoundedRectangle(cornerRadius: 10))
                }

                Bloque(titulo: "Trimestre en curso · \(enCurso.nombre)") {
                    Ficha("Facturado (base)", ingresos.filter { enCurso.contiene($0.fecha) }.suma(\.base).euros)
                    Ficha("IVA repercutido", liquidacion.totalDevengado.euros)
                    Ficha("IVA deducible", liquidacion.totalDeducir.euros)
                    Ficha("303 estimado", liquidacion.resultado.euros,
                          nota: liquidacion.resultado >= 0 ? "a ingresar" : "a compensar")
                }

                Bloque(titulo: "Ejercicio \(String(ejercicio))") {
                    let rendimiento = ingresosAño.suma(\.base) - gastosAño.suma(\.gastoIRPF)
                    Ficha("Ingresos (base)", ingresosAño.suma(\.base).euros)
                    Ficha("Gastos deducibles IRPF", gastosAño.suma(\.gastoIRPF).euros)
                    Ficha("Rendimiento neto", rendimiento.euros)
                    Ficha("Retenciones soportadas", ingresosAño.suma(\.retencion).euros,
                          nota: "a descontar en la renta")
                }

                Bloque(titulo: "Cobros") {
                    Ficha("Pendiente de cobro", pendientes.suma(\.total).euros,
                          nota: pendientes.isEmpty ? "todo cobrado" : "\(pendientes.count) factura(s)")
                }

                if ingresos.isEmpty && gastos.isEmpty {
                    ContentUnavailableView {
                        Label("Aún no hay datos", systemImage: "tray")
                    } description: {
                        Text("Emite la factura en la app gratuita de la AEAT y regístrala aquí en Ingresos. Apunta tus compras en Gastos.")
                    } actions: {
                        Button("Registrar un ingreso") { irA(.ingresos) }
                    }
                }
            }
            .padding(28)
            .frame(maxWidth: .infinity, alignment: .leading)
        }
        .navigationTitle("Panel")
    }
}

private struct TarjetaPlazo: View {
    let trimestre: Trimestre
    let hoy: Date
    let modelos: [ModeloPresentado]
    let ingresos: [DatosIngreso]
    let irAModelos: () -> Void

    var body: some View {
        let plazo = trimestre.plazoPresentacion
        let dias = Calendar.fiscal.dateComponents([.day], from: Calendar.fiscal.startOfDay(for: hoy), to: plazo).day ?? 0
        let presentado303 = modelos.first { $0.corresponde(a: .m303, trimestre) }
        let presentado130 = modelos.first { $0.corresponde(a: .m130, trimestre) }
        let conRetencion = Liquidacion130.porcentajeConRetencion(ingresos, ejercicio: trimestre.ejercicio - 1)
            ?? Liquidacion130.porcentajeConRetencion(ingresos, ejercicio: trimestre.ejercicio)
        let exento130 = (conRetencion ?? 0) >= 70

        HStack(alignment: .top, spacing: 20) {
            Image(systemName: "calendar.badge.clock")
                .font(.system(size: 34))
                .foregroundStyle(presentado303 == nil && dias <= 7 ? .orange : .accentColor)
            VStack(alignment: .leading, spacing: 8) {
                Text("Declaraciones del \(trimestre.nombre)").font(.title3.bold())
                Text(textoPlazo(plazo: plazo, dias: dias)).foregroundStyle(.secondary)
                Estado(titulo: String(localized: "Modelo 303"), modelo: presentado303)
                if exento130 {
                    Label("Modelo 130: no obligatorio (\(conRetencion!.porcentaje) de ingresos con retención)",
                          systemImage: "minus.circle").foregroundStyle(.secondary)
                } else {
                    Estado(titulo: String(localized: "Modelo 130"), modelo: presentado130)
                }
                if trimestre.numero == 4 {
                    Label("Con el 4T: modelo 390 (resumen anual de IVA)", systemImage: "info.circle")
                        .foregroundStyle(.secondary)
                }
            }
            Spacer()
            Button("Ir a modelos", action: irAModelos)
        }
        .padding(20)
        .background(.quaternary.opacity(0.5), in: RoundedRectangle(cornerRadius: 12))
    }

    private func textoPlazo(plazo: Date, dias: Int) -> String {
        let fecha = plazo.formatted(.dateTime.weekday(.wide).day().month(.wide))
        switch dias {
        case ..<0: return String(localized: "El plazo terminó el \(fecha).")
        case 0: return String(localized: "El plazo termina hoy.")
        default: return String(localized: "Plazo hasta el \(fecha) · quedan \(dias) días.")
        }
    }
}

private struct Estado: View {
    let titulo: String
    let modelo: ModeloPresentado?

    var body: some View {
        if let modelo {
            Label("\(titulo): presentado el \(modelo.fechaPresentacion.corta) · \(modelo.tipoResultado.nombre.lowercased()) \(modelo.importe.euros)",
                  systemImage: "checkmark.circle.fill")
                .foregroundStyle(.green)
        } else {
            Label("\(titulo): pendiente", systemImage: "circle.dashed").foregroundStyle(.orange)
        }
    }
}

private struct Bloque<Contenido: View>: View {
    let titulo: LocalizedStringKey
    @ViewBuilder let contenido: Contenido

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            Text(titulo).font(.headline)
            LazyVGrid(columns: [GridItem(.adaptive(minimum: 190), spacing: 12)], spacing: 12) {
                contenido
            }
        }
    }
}

private struct Ficha: View {
    let titulo: LocalizedStringKey
    let valor: String
    var nota: LocalizedStringKey?

    init(_ titulo: LocalizedStringKey, _ valor: String, nota: LocalizedStringKey? = nil) {
        self.titulo = titulo
        self.valor = valor
        self.nota = nota
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            Text(titulo).font(.subheadline).foregroundStyle(.secondary)
            Text(valor).font(.title2.weight(.semibold)).monospacedDigit()
            Text(nota ?? " ").font(.caption).foregroundStyle(.tertiary)
        }
        .padding(14)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(.quaternary.opacity(0.35), in: RoundedRectangle(cornerRadius: 10))
    }
}
