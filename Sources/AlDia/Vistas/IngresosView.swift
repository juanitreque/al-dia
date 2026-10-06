import SwiftData
import SwiftUI
import AlDiaCore

struct IngresosView: View {
    @Environment(\.modelContext) private var contexto
    @Query(sort: \Ingreso.fecha, order: .reverse) private var ingresos: [Ingreso]

    @State private var periodo = Periodo(ejercicio: Calendar.fiscal.component(.year, from: .now))
    @State private var seleccion = Set<PersistentIdentifier>()
    @State private var hoja: HojaIngreso?
    @State private var aEliminar: [Ingreso] = []
    @State private var exportando = false
    @State private var importando = false

    private var filtrados: [Ingreso] { ingresos.filter { periodo.contiene($0.fecha) } }

    var body: some View {
        let lista = filtrados
        let emitidas = lista.emitidas
        let borradores = lista.count - emitidas.count
        VStack(spacing: 0) {
            Table(lista, selection: $seleccion) {
                TableColumn("Fecha") { Text($0.fecha.corta).monospacedDigit() }.width(85)
                TableColumn("Nº") { Text($0.numero) }.width(min: 70, ideal: 90)
                TableColumn("Cliente") { Text($0.cliente?.nombre ?? "—") }
                TableColumn("Concepto") { Text($0.concepto).foregroundStyle(.secondary) }
                TableColumn("Base") { CeldaImporte(valor: $0.base) }.width(90)
                TableColumn("IVA") { CeldaImporte(valor: $0.cuotaIVA) }.width(85)
                TableColumn("Retención") { CeldaImporte(valor: $0.retencion) }.width(85)
                TableColumn("Total") { CeldaImporte(valor: $0.total).fontWeight(.medium) }.width(95)
                TableColumn("Estado") { i in
                    HStack(spacing: 4) {
                        if i.esBorrador {
                            Text("Borrador")
                                .font(.caption.weight(.medium))
                                .padding(.horizontal, 6).padding(.vertical, 1)
                                .background(.orange.opacity(0.2), in: Capsule())
                                .foregroundStyle(.orange)
                        } else {
                            Image(systemName: i.cobrada ? "checkmark.circle.fill" : "clock")
                                .foregroundStyle(i.cobrada ? .green : .orange)
                                .help(i.cobrada ? Text("Cobrada") : Text("Pendiente de cobro"))
                        }
                        if i.adjuntoNombre != nil { Image(systemName: "paperclip").foregroundStyle(.secondary) }
                    }
                }.width(80)
            }
            .contextMenu(forSelectionType: PersistentIdentifier.self) { ids in
                let elegidos = lista.filter { ids.contains($0.id) }
                if elegidos.count == 1, let i = elegidos.first {
                    if i.esBorrador {
                        Button("Emitir con Al Día (PDF)…") { hoja = .emitirPropia(i) }
                        Button("Emitir en la AEAT…") { hoja = .emitir(i) }
                        Divider()
                    }
                    Button("Editar…") { hoja = .editar(i) }
                    Button("Duplicar como borrador…") { hoja = .duplicar(i) }
                    if !i.esBorrador {
                        Button(i.cobrada ? LocalizedStringKey("Marcar como pendiente") : LocalizedStringKey("Marcar como cobrada")) { alternarCobro(i) }
                    }
                    if let adjunto = i.adjunto, let nombre = i.adjuntoNombre {
                        Button("Abrir documento") { Adjuntos.abrir(adjunto, nombre: nombre) }
                        if !i.esBorrador, !adjunto.isEmpty {
                            Button("Enviar por correo…") { FacturaPDF.enviarPorCorreo(i) }
                        }
                    }
                    Divider()
                }
                if !elegidos.isEmpty {
                    Button("Eliminar…", role: .destructive) { aEliminar = elegidos }
                }
            } primaryAction: { ids in
                guard let i = lista.first(where: { ids.contains($0.id) }) else { return }
                if i.esBorrador {
                    hoja = VeriFactu.obliga(en: i.fecha) ? .emitir(i) : .emitirPropia(i)
                } else {
                    hoja = .editar(i)
                }
            }
            .overlay {
                if lista.isEmpty {
                    ContentUnavailableView {
                        Label("Sin ingresos en \(periodo.nombre)", systemImage: "doc.text")
                    } description: {
                        Text("Prepara una factura con + o importa los PDF de tus facturas anteriores.")
                    } actions: {
                        Button("Importar facturas PDF…") { importando = true }
                    }
                }
            }

            BarraTotales(elementos: [
                ("Emitidas", "\(emitidas.count)"),
                ("Base", emitidas.suma(\.base).euros),
                ("IVA", emitidas.suma(\.cuotaIVA).euros),
                ("Retenciones", emitidas.suma(\.retencion).euros),
                ("Total", emitidas.suma(\.total).euros),
                ("Pendiente", emitidas.filter { !$0.cobrada }.suma(\.total).euros),
            ] + (borradores > 0 ? [("Borradores", "\(borradores)")] : []))
        }
        .navigationTitle("Ingresos")
        .toolbar {
            ToolbarItemGroup {
                SelectorPeriodo(periodo: $periodo, años: añosDisponibles(ingresos.map(\.fecha)))
                Button { importando = true } label: { Label("Importar PDF", systemImage: "square.and.arrow.down") }
                    .help("Importar facturas desde sus PDF")
                Button { exportando = true } label: { Label("Exportar CSV", systemImage: "square.and.arrow.up") }
                    .help("Libro de facturas emitidas (CSV para Excel)")
                Button { hoja = .nuevo } label: { Label("Nueva factura", systemImage: "plus") }
                    .keyboardShortcut("n")
                    .help("Preparar una factura (borrador)")
            }
        }
        .sheet(item: $hoja) { h in
            switch h {
            case .nuevo: IngresoEditor()
            case .editar(let i): IngresoEditor(ingreso: i)
            case .duplicar(let i): IngresoEditor(plantilla: i)
            case .emitir(let i): FichaEmisionView(ingreso: i)
            case .emitirPropia(let i): EmitirConAlDiaView(ingreso: i)
            }
        }
        .confirmationDialog("¿Eliminar \(aEliminar.count) factura(s)?", isPresented: .init(
            get: { !aEliminar.isEmpty }, set: { if !$0 { aEliminar = [] } }
        )) {
            Button("Eliminar", role: .destructive) {
                aEliminar.forEach(contexto.delete)
                try? contexto.save()
                aEliminar = []
            }
        } message: {
            Text("Solo se borra de Al Día; una factura ya emitida en la AEAT no cambia.")
        }
        .fileExporter(isPresented: $exportando, document: csv(emitidas), contentType: .commaSeparatedText,
                      defaultFilename: String(localized: "Ingresos \(periodo.nombre).csv")) { _ in }
        .importacionDeFacturas(isPresented: $importando)
    }

    private func alternarCobro(_ i: Ingreso) {
        i.cobrada.toggle()
        i.fechaCobro = i.cobrada ? (i.fechaCobro ?? .now) : nil
        try? contexto.save()
    }

    private func csv(_ lista: [Ingreso]) -> DocumentoCSV {
        var filas = [["Fecha expedición", "Número", "NIF destinatario", "Destinatario", "Concepto",
                      "Base imponible", "Tipo IVA", "Cuota IVA", "Tipo retención", "Retención", "Total",
                      "Cobrada", "Fecha cobro"]]
        for i in lista.sorted(by: { $0.fecha < $1.fecha }) {
            filas.append([
                i.fecha.corta, i.numero, i.cliente?.nif ?? "", i.cliente?.nombre ?? "", i.concepto,
                DocumentoCSV.importe(i.base), DocumentoCSV.importe(i.tipoIVA), DocumentoCSV.importe(i.cuotaIVA),
                DocumentoCSV.importe(i.tipoRetencion), DocumentoCSV.importe(i.retencion), DocumentoCSV.importe(i.total),
                i.cobrada ? "Sí" : "No", i.fechaCobro?.corta ?? "",
            ])
        }
        return DocumentoCSV(filas: filas)
    }
}

enum HojaIngreso: Identifiable {
    case nuevo
    case editar(Ingreso)
    case duplicar(Ingreso)
    case emitir(Ingreso)
    case emitirPropia(Ingreso)

    var id: String {
        switch self {
        case .nuevo: "nuevo"
        case .editar(let i): "editar-\(i.id.hashValue)"
        case .duplicar(let i): "duplicar-\(i.id.hashValue)"
        case .emitir(let i): "emitir-\(i.id.hashValue)"
        case .emitirPropia(let i): "emitir-propia-\(i.id.hashValue)"
        }
    }
}

/// Línea en edición (se vuelca a `LineaIngreso` al guardar).
struct LineaEditable: Identifiable, Equatable {
    let id = UUID()
    var codigo: String
    var concepto: String
    var cantidad: Decimal
    var precio: Decimal

    var importe: Decimal { (cantidad * precio).redondeado() }
}

func conceptoSugerido(_ fecha: Date) -> String {
    String(localized: "Servicios \(fecha.formatted(.dateTime.month(.wide).year()))")
}

struct IngresoEditor: View {
    @Environment(\.modelContext) private var contexto
    @Query(sort: \Cliente.nombre) private var clientes: [Cliente]
    @Query(sort: \Servicio.codigo) private var servicios: [Servicio]
    @Query(sort: \Ingreso.fecha) private var todos: [Ingreso]

    private let ingreso: Ingreso?
    @State private var estado: EstadoFactura
    @State private var numero: String
    @State private var numeroSugerido = ""
    @State private var fecha: Date
    @State private var cliente: Cliente?
    @State private var concepto: String
    @State private var lineas: [LineaEditable]
    @State private var base: Decimal
    @State private var tipoIVA: Decimal
    @State private var cuotaIVA: Decimal
    @State private var tipoRetencion: Decimal
    @State private var retencion: Decimal
    @State private var cobrada: Bool
    @State private var fechaCobro: Date
    @State private var notas: String
    @State private var adjunto: Data?
    @State private var adjuntoNombre: String?

    /// - `ingreso`: el registro a editar (nil = nuevo borrador).
    /// - `plantilla`: nuevo borrador copiando cliente, líneas e importes.
    init(ingreso: Ingreso? = nil, plantilla: Ingreso? = nil) {
        self.ingreso = ingreso
        let origen = ingreso ?? plantilla
        let hoy = Date.now
        _estado = State(initialValue: ingreso?.estado ?? .borrador)
        _numero = State(initialValue: ingreso?.numero ?? "")
        _fecha = State(initialValue: ingreso?.fecha ?? hoy)
        _cliente = State(initialValue: origen?.cliente)
        _concepto = State(initialValue: ingreso?.concepto ?? conceptoSugerido(hoy))
        _lineas = State(initialValue: (origen?.lineasOrdenadas ?? []).map {
            LineaEditable(codigo: $0.codigo, concepto: $0.concepto, cantidad: $0.cantidad, precio: $0.precio)
        })
        _base = State(initialValue: origen?.base ?? 0)
        _tipoIVA = State(initialValue: origen?.tipoIVA ?? Ajustes.ivaPorDefecto)
        _cuotaIVA = State(initialValue: origen?.cuotaIVA ?? 0)
        _tipoRetencion = State(initialValue: origen?.tipoRetencion ?? Ajustes.retencionPorDefecto)
        _retencion = State(initialValue: origen?.retencion ?? 0)
        _cobrada = State(initialValue: ingreso?.cobrada ?? false)
        _fechaCobro = State(initialValue: ingreso?.fechaCobro ?? ingreso?.fecha ?? hoy)
        _notas = State(initialValue: ingreso?.notas ?? "")
        _adjunto = State(initialValue: ingreso?.adjunto)
        _adjuntoNombre = State(initialValue: ingreso?.adjuntoNombre)
    }

    var body: some View {
        VStack(spacing: 0) {
            Form {
                Section {
                    Picker("Estado", selection: $estado) {
                        ForEach(EstadoFactura.allCases) { Text($0.nombre).tag($0) }
                    }
                    .pickerStyle(.segmented)
                    TextField("Número", text: $numero, prompt: Text("2026-001"))
                    DatePicker("Fecha de expedición", selection: $fecha, displayedComponents: .date)
                    Picker("Cliente", selection: $cliente) {
                        Text("Sin cliente").tag(Cliente?.none)
                        ForEach(clientes) { Text($0.nombre).tag(Cliente?.some($0)) }
                    }
                    TextField("Descripción", text: $concepto, prompt: Text("Servicios octubre 2026"))
                } header: {
                    Text("Factura")
                } footer: {
                    if estado == .borrador {
                        Text("Un borrador no cuenta para impuestos. Al emitirlo en la AEAT pondrás el número oficial.")
                            .foregroundStyle(.secondary)
                    }
                }

                Section("Líneas") {
                    ForEach($lineas) { $linea in
                        HStack(spacing: 8) {
                            TextField("Concepto", text: $linea.concepto).labelsHidden()
                            CampoImporte(titulo: "Cantidad", valor: $linea.cantidad, decimales: 0...2)
                                .labelsHidden().frame(width: 54)
                            Text("×").foregroundStyle(.secondary)
                            CampoImporte(titulo: "Precio", valor: $linea.precio).labelsHidden().frame(width: 70)
                            Text(linea.importe.euros).monospacedDigit().frame(width: 84, alignment: .trailing)
                            Button { lineas.removeAll { $0.id == linea.id } } label: {
                                Image(systemName: "minus.circle")
                            }
                            .buttonStyle(.borderless)
                            .help("Quitar línea")
                        }
                    }
                    Menu("Añadir línea") {
                        ForEach(servicios) { s in
                            Button("\(s.codigo) · \(s.descripcion) · \(s.precio.euros)") { añadir(s) }
                        }
                        if !servicios.isEmpty { Divider() }
                        Button("Línea libre") {
                            lineas.append(LineaEditable(codigo: "", concepto: "", cantidad: 1, precio: 0))
                        }
                    }
                    .fixedSize()
                }

                Section("Importes") {
                    if lineas.isEmpty {
                        CampoImporte(titulo: "Base imponible", valor: $base)
                    } else {
                        LabeledContent("Base imponible") { Text(base.euros).monospacedDigit() }
                    }
                    SelectorTipo(titulo: "Tipo de IVA", opciones: Tipos.iva, valor: $tipoIVA)
                    CampoImporte(titulo: "Cuota de IVA", valor: $cuotaIVA)
                    SelectorTipo(titulo: "Retención IRPF", opciones: Tipos.retencion, valor: $tipoRetencion)
                    CampoImporte(titulo: "Importe retenido", valor: $retencion)
                    LabeledContent("Total a cobrar") {
                        Text((base + cuotaIVA - retencion).euros).monospacedDigit().bold()
                    }
                }

                if estado == .emitida {
                    Section("Cobro") {
                        Toggle("Cobrada", isOn: $cobrada)
                        if cobrada {
                            DatePicker("Fecha de cobro", selection: $fechaCobro, displayedComponents: .date)
                        }
                    }
                }

                Section {
                    CampoAdjunto(datos: $adjunto, nombre: $adjuntoNombre)
                    TextField("Notas", text: $notas, axis: .vertical).lineLimit(2...4)
                }
            }
            .formStyle(.grouped)

            BotoneraEditor(puedeGuardar: !numero.trimmingCharacters(in: .whitespaces).isEmpty && base != 0,
                           guardar: guardar)
        }
        .frame(width: 600, height: 660)
        .onAppear {
            if ingreso == nil {
                if cliente == nil, clientes.count == 1 { cliente = clientes.first }
                sugerirNumero()
            }
        }
        .onChange(of: fecha) { vieja, nueva in
            guard ingreso == nil else { return }
            if concepto == conceptoSugerido(vieja) { concepto = conceptoSugerido(nueva) }
            sugerirNumero()
        }
        .onChange(of: lineas) { _, nuevas in
            if !nuevas.isEmpty { base = nuevas.map(\.importe).reduce(0, +) }
        }
        .onChange(of: base) { recalcular() }
        .onChange(of: tipoIVA) { recalcular() }
        .onChange(of: tipoRetencion) { recalcular() }
    }

    private func añadir(_ s: Servicio) {
        lineas.append(LineaEditable(codigo: s.codigo, concepto: s.descripcion, cantidad: 1, precio: s.precio))
        tipoIVA = s.tipoIVA
    }

    /// Propone el siguiente número mientras el usuario no lo haya cambiado a mano.
    private func sugerirNumero() {
        guard numero.isEmpty || numero == numeroSugerido else { return }
        let anteriores = todos.filter { $0.fecha <= fecha }.map(\.numero)
        numeroSugerido = Ajustes.numeroSugerido(fecha: fecha, anteriores: anteriores, existentes: todos.map(\.numero))
        numero = numeroSugerido
    }

    private func recalcular() {
        cuotaIVA = Calculo.porcentaje(tipoIVA, de: base)
        retencion = Calculo.porcentaje(tipoRetencion, de: base)
    }

    private func guardar() {
        let destino = ingreso ?? {
            let nuevo = Ingreso()
            contexto.insert(nuevo)
            return nuevo
        }()
        destino.estado = estado
        destino.numero = numero.trimmingCharacters(in: .whitespaces)
        destino.fecha = fecha
        destino.cliente = cliente
        destino.concepto = concepto
        destino.base = base
        destino.tipoIVA = tipoIVA
        destino.cuotaIVA = cuotaIVA
        destino.tipoRetencion = tipoRetencion
        destino.retencion = retencion
        destino.cobrada = estado == .emitida && cobrada
        destino.fechaCobro = destino.cobrada ? fechaCobro : nil
        destino.notas = notas
        destino.adjunto = adjunto
        destino.adjuntoNombre = adjuntoNombre

        for vieja in destino.lineas ?? [] { contexto.delete(vieja) }
        destino.lineas = lineas.enumerated().map { orden, l in
            LineaIngreso(orden: orden, codigo: l.codigo, concepto: l.concepto, cantidad: l.cantidad, precio: l.precio)
        }
        try? contexto.save()
    }
}
