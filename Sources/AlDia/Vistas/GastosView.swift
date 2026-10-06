import SwiftData
import SwiftUI
import UniformTypeIdentifiers
import AlDiaCore

struct GastosView: View {
    @Environment(\.modelContext) private var contexto
    @Query(sort: \Gasto.fecha, order: .reverse) private var gastos: [Gasto]

    @State private var periodo = Periodo(ejercicio: Calendar.fiscal.component(.year, from: .now))
    @State private var seleccion = Set<PersistentIdentifier>()
    @State private var hoja: HojaGasto?
    @State private var aEliminar: [Gasto] = []
    @State private var exportando = false
    @State private var importando = false
    @State private var leyendo = false
    @State private var cola: [GastoImportado] = []

    private var filtrados: [Gasto] { gastos.filter { periodo.contiene($0.fecha) } }

    var body: some View {
        let lista = filtrados
        let datos = lista.map(\.datos)
        VStack(spacing: 0) {
            Table(lista, selection: $seleccion) {
                TableColumn("Fecha") { Text($0.fecha.corta).monospacedDigit() }.width(85)
                TableColumn("Proveedor") { Text($0.proveedor) }
                TableColumn("Concepto") { Text($0.concepto).foregroundStyle(.secondary) }
                TableColumn("Categoría") { Text($0.categoria.nombre).foregroundStyle(.secondary) }
                TableColumn("Base") { CeldaImporte(valor: $0.base) }.width(90)
                TableColumn("IVA") { CeldaImporte(valor: $0.cuotaIVA) }.width(80)
                TableColumn("IVA deducible") { CeldaImporte(valor: $0.datos.ivaDeducible) }.width(95)
                TableColumn("Gasto IRPF") { CeldaImporte(valor: $0.datos.gastoIRPF) }.width(90)
                TableColumn("") { g in
                    HStack(spacing: 4) {
                        if !g.facturaCompleta {
                            Image(systemName: "exclamationmark.triangle").foregroundStyle(.orange)
                                .help("Sin factura completa: el IVA no es deducible")
                        }
                        if g.bienInversion {
                            Image(systemName: "shippingbox").foregroundStyle(.secondary).help("Bien de inversión")
                        }
                        if g.adjuntoNombre != nil { Image(systemName: "paperclip").foregroundStyle(.secondary) }
                    }
                }.width(56)
            }
            .contextMenu(forSelectionType: PersistentIdentifier.self) { ids in
                let elegidos = lista.filter { ids.contains($0.id) }
                if elegidos.count == 1, let g = elegidos.first {
                    Button("Editar…") { hoja = .editar(g) }
                    Button("Duplicar…") { hoja = .duplicar(g) }
                    if let adjunto = g.adjunto, let nombre = g.adjuntoNombre {
                        Button("Abrir documento") { Adjuntos.abrir(adjunto, nombre: nombre) }
                    }
                    Divider()
                }
                if !elegidos.isEmpty {
                    Button("Eliminar…", role: .destructive) { aEliminar = elegidos }
                }
            } primaryAction: { ids in
                if let g = lista.first(where: { ids.contains($0.id) }) { hoja = .editar(g) }
            }
            .overlay {
                if leyendo {
                    ProgressView("Leyendo el documento…")
                        .padding(24)
                        .background(.regularMaterial, in: RoundedRectangle(cornerRadius: 12))
                } else if lista.isEmpty {
                    ContentUnavailableView {
                        Label("Sin gastos en \(periodo.nombre)", systemImage: "cart")
                    } description: {
                        Text("Pulsa + para apuntar una compra, o importa la foto o el PDF de un ticket o factura.")
                    } actions: {
                        Button("Importar ticket o factura…") { importando = true }
                    }
                }
            }
            .dropDestination(for: URL.self) { urls, _ in
                let documentos = urls.filter(GastoImportado.admite)
                guard !documentos.isEmpty else { return false }
                Task { await leer(documentos) }
                return true
            }

            BarraTotales(elementos: [
                ("Gastos", "\(lista.count)"),
                ("Base", lista.suma(\.base).euros),
                ("IVA soportado", lista.suma(\.cuotaIVA).euros),
                ("IVA deducible", datos.suma(\.ivaDeducible).euros),
                ("Gasto IRPF", datos.suma(\.gastoIRPF).euros),
            ])
        }
        .navigationTitle("Gastos")
        .toolbar {
            ToolbarItemGroup {
                SelectorPeriodo(periodo: $periodo, años: añosDisponibles(gastos.map(\.fecha)))
                Button { importando = true } label: { Label("Importar ticket o factura", systemImage: "doc.text.viewfinder") }
                    .help("Rellenar gastos leyendo la foto o el PDF de sus tickets o facturas")
                Button { exportando = true } label: { Label("Exportar CSV", systemImage: "square.and.arrow.up") }
                    .help("Libro de facturas recibidas y gastos (CSV para Excel)")
                Button { hoja = .nuevo } label: { Label("Nuevo gasto", systemImage: "plus") }
                    .keyboardShortcut("n")
            }
        }
        .sheet(item: $hoja) { h in
            switch h {
            case .nuevo: GastoEditor()
            case .editar(let g): GastoEditor(gasto: g)
            case .duplicar(let g): GastoEditor(plantilla: g)
            case .importado(let i): GastoEditor(importado: i)
            }
        }
        .onChange(of: hoja?.id) { _, nuevo in
            if nuevo == nil { mostrarSiguienteImportado() }
        }
        .fileImporter(isPresented: $importando, allowedContentTypes: [.pdf, .image], allowsMultipleSelection: true) { resultado in
            guard case .success(let urls) = resultado, !urls.isEmpty else { return }
            Task { await leer(urls) }
        }
        .confirmationDialog("¿Eliminar \(aEliminar.count) gasto(s)?", isPresented: .init(
            get: { !aEliminar.isEmpty }, set: { if !$0 { aEliminar = [] } }
        )) {
            Button("Eliminar", role: .destructive) {
                aEliminar.forEach(contexto.delete)
                try? contexto.save()
                aEliminar = []
            }
        }
        .fileExporter(isPresented: $exportando, document: Libros.gastos(lista), contentType: .commaSeparatedText,
                      defaultFilename: String(localized: "Gastos \(periodo.nombre).csv")) { _ in }
    }

    /// Lee los documentos y los pone en cola; cada uno se abre en el editor para revisarlo.
    private func leer(_ urls: [URL]) async {
        leyendo = true
        let miNIF = UserDefaults.standard.string(forKey: Ajustes.nif) ?? ""
        let miNombre = UserDefaults.standard.string(forKey: Ajustes.nombre) ?? ""
        for url in urls {
            if let importado = await GastoImportado.leer(url, miNIF: miNIF, miNombre: miNombre) { cola.append(importado) }
        }
        leyendo = false
        if hoja == nil { mostrarSiguienteImportado() }
    }

    private func mostrarSiguienteImportado() {
        guard !cola.isEmpty else { return }
        hoja = .importado(cola.removeFirst())
    }

}

enum HojaGasto: Identifiable {
    case nuevo
    case editar(Gasto)
    case duplicar(Gasto)
    case importado(GastoImportado)

    var id: String {
        switch self {
        case .nuevo: "nuevo"
        case .editar(let g): "editar-\(g.id.hashValue)"
        case .duplicar(let g): "duplicar-\(g.id.hashValue)"
        case .importado(let i): "importado-\(i.id)"
        }
    }
}

/// Ticket o factura leído de un archivo, pendiente de revisar en el editor.
struct GastoImportado: Identifiable {
    let id = UUID()
    let leido: GastoLeido
    let datos: Data
    let nombre: String
    /// Hay un NIF propio configurado en Ajustes con el que comprobar si la factura es a tu nombre.
    let conMiNIF: Bool

    static func admite(_ url: URL) -> Bool {
        guard let tipo = UTType(filenameExtension: url.pathExtension) else { return false }
        return tipo.conforms(to: .pdf) || tipo.conforms(to: .image)
    }

    static func leer(_ url: URL, miNIF: String, miNombre: String) async -> GastoImportado? {
        let acceso = url.startAccessingSecurityScopedResource()
        defer { if acceso { url.stopAccessingSecurityScopedResource() } }
        guard let datos = try? Data(contentsOf: url) else { return nil }
        let esPDF = UTType(filenameExtension: url.pathExtension)?.conforms(to: .pdf) ?? false
        let texto = await LecturaDocumentos.texto(de: datos, esPDF: esPDF)
        return GastoImportado(leido: LectorTicket.analizar(texto, miNIF: miNIF, miNombre: miNombre), datos: datos,
                              nombre: url.lastPathComponent, conMiNIF: !miNIF.isEmpty)
    }
}

struct GastoEditor: View {
    @Environment(\.modelContext) private var contexto

    private let gasto: Gasto?
    @State private var fecha: Date
    @State private var proveedor: String
    @State private var nifProveedor: String
    @State private var numeroFactura: String
    @State private var concepto: String
    @State private var categoria: CategoriaGasto
    @State private var base: Decimal
    @State private var tipoIVA: Decimal
    @State private var cuotaIVA: Decimal
    @State private var porcentajeDeducible: Decimal
    @State private var facturaCompleta: Bool
    @State private var deducibleIRPF: Bool
    @State private var bienInversion: Bool
    @State private var notas: String
    @State private var adjunto: Data?
    @State private var adjuntoNombre: String?
    private var importado = false
    private var proveedorExtranjero = false

    init(gasto: Gasto? = nil, plantilla: Gasto? = nil) {
        self.gasto = gasto
        let origen = gasto ?? plantilla
        _fecha = State(initialValue: gasto?.fecha ?? .now)
        _proveedor = State(initialValue: origen?.proveedor ?? "")
        _nifProveedor = State(initialValue: origen?.nifProveedor ?? "")
        _numeroFactura = State(initialValue: gasto?.numeroFactura ?? "")
        _concepto = State(initialValue: origen?.concepto ?? "")
        _categoria = State(initialValue: origen?.categoria ?? .material)
        _base = State(initialValue: origen?.base ?? 0)
        _tipoIVA = State(initialValue: origen?.tipoIVA ?? 21)
        _cuotaIVA = State(initialValue: origen?.cuotaIVA ?? 0)
        _porcentajeDeducible = State(initialValue: origen?.porcentajeDeducibleIVA ?? 100)
        _facturaCompleta = State(initialValue: origen?.facturaCompleta ?? true)
        _deducibleIRPF = State(initialValue: origen?.deducibleIRPF ?? true)
        _bienInversion = State(initialValue: origen?.bienInversion ?? false)
        _notas = State(initialValue: gasto?.notas ?? "")
        _adjunto = State(initialValue: gasto?.adjunto)
        _adjuntoNombre = State(initialValue: gasto?.adjuntoNombre)
    }

    /// Gasto nuevo con los datos leídos de un ticket o factura.
    init(importado i: GastoImportado) {
        self.init()
        let l = i.leido
        importado = true
        proveedorExtranjero = l.proveedorExtranjero
        _fecha = State(initialValue: l.fecha ?? .now)
        _proveedor = State(initialValue: l.proveedor)
        _nifProveedor = State(initialValue: l.nifProveedor)
        _numeroFactura = State(initialValue: l.numeroFactura)
        _categoria = State(initialValue: .otros)
        _base = State(initialValue: l.base)
        _tipoIVA = State(initialValue: l.tipoIVA)
        _cuotaIVA = State(initialValue: l.cuotaIVA)
        _facturaCompleta = State(initialValue: i.conMiNIF ? l.aMiNombre : true)
        _adjunto = State(initialValue: i.datos)
        _adjuntoNombre = State(initialValue: i.nombre)
    }

    private var datos: DatosGasto {
        DatosGasto(fecha: fecha, base: base, cuotaIVA: cuotaIVA, porcentajeDeducibleIVA: porcentajeDeducible,
                   facturaCompleta: facturaCompleta, deducibleIRPF: deducibleIRPF, bienInversion: bienInversion)
    }

    var body: some View {
        VStack(spacing: 0) {
            Form {
                if importado {
                    Section {
                        Label("Datos leídos del documento: revísalos antes de guardar, sobre todo importes y fecha.",
                              systemImage: "doc.text.viewfinder")
                            .foregroundStyle(.secondary)
                        if proveedorExtranjero {
                            Label("Proveedor de otro país de la UE: aunque la factura lleve IVA español, puede no ser deducible como el de un proveedor nacional (inversión del sujeto pasivo). Consúltalo con tu gestor.",
                                  systemImage: "exclamationmark.triangle")
                                .foregroundStyle(.orange)
                        }
                    }
                }
                Section("Compra") {
                    DatePicker("Fecha", selection: $fecha, displayedComponents: .date)
                    TextField("Proveedor", text: $proveedor, prompt: Text("Amazon, Movistar…"))
                    TextField("NIF proveedor", text: $nifProveedor)
                    TextField("Nº de factura", text: $numeroFactura)
                    TextField("Concepto", text: $concepto)
                    Picker("Categoría", selection: $categoria) {
                        ForEach(CategoriaGasto.allCases) { Text($0.nombre).tag($0) }
                    }
                }
                Section("Importes") {
                    CampoImporte(titulo: "Base imponible", valor: $base)
                    SelectorTipo(titulo: "Tipo de IVA", opciones: Tipos.iva, valor: $tipoIVA)
                    CampoImporte(titulo: "Cuota de IVA", valor: $cuotaIVA)
                    LabeledContent("Total pagado") { Text((base + cuotaIVA).euros).monospacedDigit() }
                }
                Section {
                    Toggle("Factura completa a mi nombre y NIF", isOn: $facturaCompleta)
                    SelectorTipo(titulo: "IVA deducible", opciones: Tipos.deducible, valor: $porcentajeDeducible)
                        .disabled(!facturaCompleta || cuotaIVA == 0)
                    Toggle("Gasto deducible en IRPF", isOn: $deducibleIRPF)
                    Toggle("Bien de inversión (se amortiza)", isOn: $bienInversion)
                    LabeledContent("Deduces de IVA") { Text(datos.ivaDeducible.euros).monospacedDigit() }
                    LabeledContent("Gasto para IRPF") { Text(datos.gastoIRPF.euros).monospacedDigit() }
                } header: {
                    Text("Deducibilidad")
                } footer: {
                    Text(aviso).foregroundStyle(.secondary)
                }
                Section {
                    CampoAdjunto(datos: $adjunto, nombre: $adjuntoNombre)
                    TextField("Notas", text: $notas, axis: .vertical).lineLimit(2...4)
                }
            }
            .formStyle(.grouped)

            BotoneraEditor(puedeGuardar: !proveedor.trimmingCharacters(in: .whitespaces).isEmpty && base != 0,
                           guardar: guardar)
        }
        .frame(width: 540, height: 620)
        .onChange(of: base) { cuotaIVA = Calculo.porcentaje(tipoIVA, de: base) }
        .onChange(of: tipoIVA) { cuotaIVA = Calculo.porcentaje(tipoIVA, de: base) }
        .onChange(of: categoria) { _, nueva in
            guard gasto == nil, let s = nueva.sugerencia else { return }
            tipoIVA = s.tipoIVA
            porcentajeDeducible = s.porcentajeDeducible
        }
    }

    private var aviso: String {
        if !facturaCompleta { return String(localized: "Un ticket o factura simplificada sin tu NIF no permite deducir el IVA; pide factura completa.") }
        if bienInversion { return String(localized: "Los bienes de inversión no cuentan como gasto del año en IRPF: se amortizan (pregunta a tu gestor el coeficiente).") }
        if categoria == .vehiculo { return String(localized: "Vehículo de uso mixto: Hacienda presume afectación del 50 % para IVA; en IRPF solo si el uso es exclusivo.") }
        return String(localized: "El IVA no deducible se suma al gasto de IRPF.")
    }

    private func guardar() {
        let destino = gasto ?? {
            let nuevo = Gasto()
            contexto.insert(nuevo)
            return nuevo
        }()
        destino.fecha = fecha
        destino.proveedor = proveedor.trimmingCharacters(in: .whitespaces)
        destino.nifProveedor = nifProveedor
        destino.numeroFactura = numeroFactura
        destino.concepto = concepto
        destino.categoria = categoria
        destino.base = base
        destino.tipoIVA = tipoIVA
        destino.cuotaIVA = cuotaIVA
        destino.porcentajeDeducibleIVA = porcentajeDeducible
        destino.facturaCompleta = facturaCompleta
        destino.deducibleIRPF = deducibleIRPF
        destino.bienInversion = bienInversion
        destino.notas = notas
        destino.adjunto = adjunto
        destino.adjuntoNombre = adjuntoNombre
        try? contexto.save()
    }
}
