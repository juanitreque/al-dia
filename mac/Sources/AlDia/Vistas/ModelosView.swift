import SwiftData
import SwiftUI
import AlDiaCore

struct ModelosView: View {
    enum Pestaña: CaseIterable {
        case calcular, presentados

        var nombre: LocalizedStringKey {
            switch self {
            case .calcular: "Calcular trimestre"
            case .presentados: "Presentados"
            }
        }
    }

    @State private var pestaña = Pestaña.calcular

    var body: some View {
        Group {
            switch pestaña {
            case .calcular: CalculadoraTrimestre()
            case .presentados: ModelosPresentadosView()
            }
        }
        .navigationTitle("Modelos fiscales")
        .toolbar {
            ToolbarItem(placement: .principal) {
                Picker("Vista", selection: $pestaña) {
                    ForEach(Pestaña.allCases, id: \.self) { Text($0.nombre).tag($0) }
                }
                .pickerStyle(.segmented)
                .fixedSize()
            }
        }
    }
}

// MARK: - Calculadora

private struct Borrador: Identifiable {
    let id = UUID()
    let tipo: TipoModelo
    let trimestre: Trimestre
    let resultado: Decimal
}

private struct CalculadoraTrimestre: View {
    @Query private var todosLosIngresos: [Ingreso]
    private var ingresos: [Ingreso] { todosLosIngresos.emitidas }
    @Query private var gastos: [Gasto]
    @Query private var modelos: [ModeloPresentado]

    @State private var trimestre = Trimestre.aPresentar(hoy: .now)
    @State private var borrador: Borrador?
    @State private var paquete = false

    var body: some View {
        let datosIngresos = ingresos.map(\.datos)
        let datosGastos = gastos.map(\.datos)
        let l303 = Liquidacion303(trimestre: trimestre, ingresos: datosIngresos, gastos: datosGastos)
        let l130 = Liquidacion130(trimestre: trimestre, ingresos: datosIngresos, gastos: datosGastos,
                                  pagosAnteriores: pagos130Anteriores)
        let pendiente = pendienteCompensar303

        ScrollView {
            VStack(alignment: .leading, spacing: 20) {
                HStack {
                    Button { trimestre = trimestre.anterior } label: { Image(systemName: "chevron.left") }
                    Text(trimestre.nombre).font(.title2.bold()).monospacedDigit().frame(minWidth: 90)
                    Button { trimestre = trimestre.siguiente } label: { Image(systemName: "chevron.right") }
                    Spacer()
                    Text("Plazo: hasta el \(trimestre.plazoPresentacion.formatted(.dateTime.day().month(.wide).year()))")
                        .foregroundStyle(.secondary)
                    Button { paquete = true } label: {
                        Label("Paquete para el gestor…", systemImage: "shippingbox")
                    }
                    .help("Zip con resumen, libros y documentos del trimestre, listo para enviar por correo")
                }

                Tarjeta(titulo: "Modelo 303 · IVA", presentado: modelos.first { $0.corresponde(a: .m303, trimestre) }) {
                    Grid(alignment: .leading, horizontalSpacing: 16, verticalSpacing: 8) {
                        CabeceraCasillas()
                        if l303.devengos.isEmpty {
                            Fila(casilla: "07–09", concepto: "IVA devengado al 21 %", importe: 0)
                        }
                        ForEach(l303.devengos) { d in
                            Fila(casilla: d.casillas?.base ?? "—", concepto: "Base imponible al \(d.tipo.porcentaje)", importe: d.base)
                            Fila(casilla: d.casillas?.cuota ?? "—", concepto: "Cuota devengada al \(d.tipo.porcentaje)", importe: d.cuota)
                        }
                        Fila(casilla: "27", concepto: "Total cuota devengada", importe: l303.totalDevengado, destacado: true)
                        Divider().gridCellColumns(3)
                        Fila(casilla: "28", concepto: "Base operaciones interiores corrientes", importe: l303.baseCorrientes)
                        Fila(casilla: "29", concepto: "Cuota deducible operaciones corrientes", importe: l303.cuotaCorrientes)
                        if l303.cuotaInversion > 0 {
                            Fila(casilla: "30", concepto: "Base bienes de inversión", importe: l303.baseInversion)
                            Fila(casilla: "31", concepto: "Cuota deducible bienes de inversión", importe: l303.cuotaInversion)
                        }
                        Fila(casilla: "45", concepto: "Total a deducir", importe: l303.totalDeducir, destacado: true)
                        Divider().gridCellColumns(3)
                        Fila(casilla: "46", concepto: "Resultado régimen general", importe: l303.resultado, destacado: true)
                        if pendiente > 0 {
                            Fila(casilla: "110/78", concepto: "Cuotas a compensar de periodos anteriores", importe: -pendiente)
                            Fila(casilla: "71", concepto: "Resultado de la liquidación", importe: l303.resultado(compensando: pendiente), destacado: true)
                        }
                    }
                } pie: {
                    let final = l303.resultado(compensando: pendiente)
                    Text(sentido303(final)).foregroundStyle(.secondary)
                    Spacer()
                    Button("Registrar presentación…") {
                        borrador = Borrador(tipo: .m303, trimestre: trimestre, resultado: final)
                    }
                }

                Tarjeta(titulo: "Modelo 130 · Pago fraccionado IRPF", presentado: modelos.first { $0.corresponde(a: .m130, trimestre) }) {
                    Grid(alignment: .leading, horizontalSpacing: 16, verticalSpacing: 8) {
                        CabeceraCasillas()
                        Fila(casilla: "01", concepto: "Ingresos computables (1 ene – fin del trimestre)", importe: l130.ingresos)
                        Fila(casilla: "02", concepto: "Gastos fiscalmente deducibles", importe: l130.gastos)
                        Fila(casilla: "03", concepto: "Rendimiento neto", importe: l130.rendimiento, destacado: true)
                        Fila(casilla: "04", concepto: "20 % del rendimiento", importe: l130.cuota)
                        Fila(casilla: "05", concepto: "Pagos fraccionados de trimestres anteriores", importe: -l130.pagosAnteriores)
                        Fila(casilla: "06", concepto: "Retenciones soportadas", importe: -l130.retenciones)
                        Divider().gridCellColumns(3)
                        Fila(casilla: "07", concepto: "Pago fraccionado previo", importe: l130.resultado, destacado: true)
                    }
                } pie: {
                    Text(aviso130(l130)).foregroundStyle(.secondary)
                    Spacer()
                    Button("Registrar presentación…") {
                        borrador = Borrador(tipo: .m130, trimestre: trimestre, resultado: l130.resultado)
                    }
                }

                Text("Cálculo orientativo para revisar antes de presentar en la sede de la AEAT. Compruébalo siempre contra el formulario oficial.")
                    .font(.caption)
                    .foregroundStyle(.tertiary)
            }
            .padding(24)
        }
        .sheet(isPresented: $paquete) { PaqueteGestorView(trimestre: trimestre) }
        .sheet(item: $borrador) { b in
            ModeloEditor(tipo: b.tipo, trimestre: b.trimestre, resultado: b.resultado)
        }
    }

    private var pagos130Anteriores: Decimal { modelos.pagos130Anteriores(a: trimestre) }

    private var pendienteCompensar303: Decimal { modelos.pendienteCompensar303(en: trimestre) }

    private func sentido303(_ r: Decimal) -> String {
        if r > 0 { return String(localized: "A ingresar \(r.euros)") }
        if r == 0 { return String(localized: "Sin actividad / resultado cero") }
        return trimestre.numero == 4
            ? String(localized: "Negativo: a compensar o solicitar devolución")
            : String(localized: "Negativo: a compensar en el siguiente trimestre")
    }

    private func aviso130(_ l: Liquidacion130) -> String {
        let pct = Liquidacion130.porcentajeConRetencion(ingresos.map(\.datos), ejercicio: trimestre.ejercicio - 1)
            ?? Liquidacion130.porcentajeConRetencion(ingresos.map(\.datos), ejercicio: trimestre.ejercicio)
        if let pct, pct >= 70 {
            return String(localized: "\(pct.porcentaje) de ingresos con retención: no estás obligado a presentar el 130.")
        }
        return l.resultado > 0
            ? String(localized: "A ingresar \(l.resultado.euros)")
            : String(localized: "Declaración negativa (0 € a ingresar)")
    }
}

private struct Tarjeta<Contenido: View, Pie: View>: View {
    let titulo: LocalizedStringKey
    let presentado: ModeloPresentado?
    @ViewBuilder let contenido: Contenido
    @ViewBuilder let pie: Pie

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            HStack {
                Text(titulo).font(.headline)
                Spacer()
                if let presentado {
                    Label("Presentado el \(presentado.fechaPresentacion.corta)", systemImage: "checkmark.seal.fill")
                        .foregroundStyle(.green)
                        .font(.callout)
                }
            }
            contenido
            HStack { pie }
        }
        .padding(18)
        .background(.quaternary.opacity(0.35), in: RoundedRectangle(cornerRadius: 12))
    }
}

private struct CabeceraCasillas: View {
    var body: some View {
        GridRow {
            Text("Casilla")
            Text("Concepto")
            Text("Importe").gridColumnAlignment(.trailing)
        }
        .font(.caption)
        .foregroundStyle(.secondary)
    }
}

private struct Fila: View {
    let casilla: String
    let concepto: LocalizedStringKey
    let importe: Decimal
    var destacado = false

    var body: some View {
        GridRow {
            Text(casilla).monospacedDigit().foregroundStyle(.secondary)
            Text(concepto)
            Text(importe.euros).monospacedDigit()
        }
        .fontWeight(destacado ? .semibold : .regular)
    }
}

// MARK: - Presentados

private struct ModelosPresentadosView: View {
    @Environment(\.modelContext) private var contexto
    @Query(sort: [SortDescriptor(\ModeloPresentado.ejercicio, order: .reverse),
                  SortDescriptor(\ModeloPresentado.periodo, order: .reverse)])
    private var modelos: [ModeloPresentado]

    @State private var seleccion = Set<PersistentIdentifier>()
    @State private var editando: ModeloPresentado?
    @State private var creando = false
    @State private var aEliminar: [ModeloPresentado] = []

    var body: some View {
        Table(modelos, selection: $seleccion) {
            TableColumn("Modelo") { Text($0.tipo == .otro ? $0.modelo : $0.tipo.descripcion) }
            TableColumn("Periodo") { Text(verbatim: "\(nombrePeriodo($0.periodo)) \($0.ejercicio)").monospacedDigit() }.width(80)
            TableColumn("Resultado") { Text($0.tipoResultado.nombre) }.width(130)
            TableColumn("Importe") { CeldaImporte(valor: $0.importe) }.width(95)
            TableColumn("Presentado") { Text($0.fechaPresentacion.corta).monospacedDigit() }.width(90)
            TableColumn("Justificante") { Text($0.justificante).foregroundStyle(.secondary).textSelection(.enabled) }
            TableColumn("") { m in
                if m.adjuntoNombre != nil { Image(systemName: "paperclip").foregroundStyle(.secondary) }
            }.width(24)
        }
        .contextMenu(forSelectionType: PersistentIdentifier.self) { ids in
            let elegidos = modelos.filter { ids.contains($0.id) }
            if elegidos.count == 1, let m = elegidos.first {
                Button("Editar…") { editando = m }
                if let adjunto = m.adjunto, let nombre = m.adjuntoNombre {
                    Button("Abrir justificante") { Adjuntos.abrir(adjunto, nombre: nombre) }
                }
                Divider()
            }
            if !elegidos.isEmpty {
                Button("Eliminar…", role: .destructive) { aEliminar = elegidos }
            }
        } primaryAction: { ids in
            editando = modelos.first { ids.contains($0.id) }
        }
        .overlay {
            if modelos.isEmpty {
                ContentUnavailableView("Sin declaraciones registradas", systemImage: "building.columns",
                                       description: Text("Registra aquí cada modelo que presentes, con su justificante."))
            }
        }
        .toolbar {
            Button { creando = true } label: { Label("Registrar modelo", systemImage: "plus") }
        }
        .sheet(item: $editando) { ModeloEditor(modelo: $0) }
        .sheet(isPresented: $creando) { ModeloEditor() }
        .confirmationDialog("¿Eliminar \(aEliminar.count) registro(s)?", isPresented: .init(
            get: { !aEliminar.isEmpty }, set: { if !$0 { aEliminar = [] } }
        )) {
            Button("Eliminar", role: .destructive) {
                aEliminar.forEach(contexto.delete)
                try? contexto.save()
                aEliminar = []
            }
        }
    }
}

struct ModeloEditor: View {
    @Environment(\.modelContext) private var contexto

    private let modelo: ModeloPresentado?
    @State private var tipo: TipoModelo
    @State private var otroModelo: String
    @State private var ejercicio: Int
    @State private var periodo: String
    @State private var importe: Decimal
    @State private var tipoResultado: TipoResultado
    @State private var fecha: Date
    @State private var justificante: String
    @State private var notas: String
    @State private var adjunto: Data?
    @State private var adjuntoNombre: String?

    private static let periodos = ["1T", "2T", "3T", "4T", "0A"]

    init(modelo: ModeloPresentado? = nil) {
        self.modelo = modelo
        let t = Trimestre.aPresentar(hoy: .now)
        _tipo = State(initialValue: modelo?.tipo ?? .m303)
        _otroModelo = State(initialValue: modelo?.tipo == .otro ? modelo!.modelo : "")
        _ejercicio = State(initialValue: modelo?.ejercicio ?? t.ejercicio)
        _periodo = State(initialValue: modelo?.periodo ?? t.periodo)
        _importe = State(initialValue: modelo?.importe ?? 0)
        _tipoResultado = State(initialValue: modelo?.tipoResultado ?? .ingresar)
        _fecha = State(initialValue: modelo?.fechaPresentacion ?? .now)
        _justificante = State(initialValue: modelo?.justificante ?? "")
        _notas = State(initialValue: modelo?.notas ?? "")
        _adjunto = State(initialValue: modelo?.adjunto)
        _adjuntoNombre = State(initialValue: modelo?.adjuntoNombre)
    }

    /// Nuevo registro precargado con el resultado de la calculadora.
    init(tipo: TipoModelo, trimestre: Trimestre, resultado: Decimal) {
        self.init()
        _tipo = State(initialValue: tipo)
        _ejercicio = State(initialValue: trimestre.ejercicio)
        _periodo = State(initialValue: trimestre.periodo)
        _importe = State(initialValue: abs(resultado))
        let sentido: TipoResultado = if resultado > 0 { .ingresar }
            else if resultado == 0 || tipo == .m130 { .negativa }
            else if trimestre.numero == 4 { .devolver }
            else { .compensar }
        _tipoResultado = State(initialValue: sentido)
        if tipo == .m130 && resultado < 0 { _importe = State(initialValue: 0) }
    }

    var body: some View {
        VStack(spacing: 0) {
            Form {
                Section("Declaración") {
                    Picker("Modelo", selection: $tipo) {
                        ForEach(TipoModelo.allCases) { Text($0.descripcion).tag($0) }
                    }
                    if tipo == .otro {
                        TextField("Número de modelo", text: $otroModelo)
                    }
                    Stepper(value: $ejercicio, in: 2000...2100) {
                        LabeledContent("Ejercicio", value: String(ejercicio))
                    }
                    Picker("Periodo", selection: $periodo) {
                        ForEach(Self.periodos, id: \.self) { Text(nombrePeriodo($0)).tag($0) }
                    }
                }
                Section("Resultado") {
                    Picker("Resultado", selection: $tipoResultado) {
                        ForEach(TipoResultado.allCases) { Text($0.nombre).tag($0) }
                    }
                    CampoImporte(titulo: "Importe", valor: $importe)
                    DatePicker("Fecha de presentación", selection: $fecha, displayedComponents: .date)
                    TextField("Justificante / NRC / CSV", text: $justificante)
                }
                Section {
                    CampoAdjunto(datos: $adjunto, nombre: $adjuntoNombre)
                    TextField("Notas", text: $notas, axis: .vertical).lineLimit(2...4)
                }
            }
            .formStyle(.grouped)

            BotoneraEditor(puedeGuardar: tipo != .otro || !otroModelo.isEmpty, guardar: guardar)
        }
        .frame(width: 520, height: 600)
        .onChange(of: tipo) { _, nuevo in
            if [.m390, .m347, .m100].contains(nuevo) { periodo = "0A" }
        }
    }

    private func guardar() {
        let destino = modelo ?? {
            let nuevo = ModeloPresentado()
            contexto.insert(nuevo)
            return nuevo
        }()
        destino.modelo = tipo == .otro ? otroModelo : tipo.rawValue
        destino.ejercicio = ejercicio
        destino.periodo = periodo
        destino.importe = abs(importe)
        destino.tipoResultado = tipoResultado
        destino.fechaPresentacion = fecha
        destino.justificante = justificante
        destino.notas = notas
        destino.adjunto = adjunto
        destino.adjuntoNombre = adjuntoNombre
        try? contexto.save()
    }
}
