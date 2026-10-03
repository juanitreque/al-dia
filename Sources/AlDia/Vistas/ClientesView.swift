import SwiftData
import SwiftUI
import AlDiaCore

struct ClientesView: View {
    @Environment(\.modelContext) private var contexto
    @Query(sort: \Cliente.nombre) private var clientes: [Cliente]

    @State private var seleccion = Set<PersistentIdentifier>()
    @State private var editando: Cliente?
    @State private var creando = false
    @State private var aEliminar: [Cliente] = []
    @State private var exportando = false

    var body: some View {
        Table(clientes, selection: $seleccion) {
            TableColumn("Nombre") { Text($0.nombre) }
            TableColumn("NIF") { Text($0.nif).monospaced() }.width(110)
            TableColumn("Email") { Text($0.email).foregroundStyle(.secondary) }
            TableColumn("Facturas") { Text("\(($0.ingresos ?? []).emitidas.count)").monospacedDigit() }.width(70)
            TableColumn("Facturado (base)") { CeldaImporte(valor: ($0.ingresos ?? []).emitidas.suma(\.base)) }.width(130)
        }
        .contextMenu(forSelectionType: PersistentIdentifier.self) { ids in
            let elegidos = clientes.filter { ids.contains($0.id) }
            if elegidos.count == 1, let c = elegidos.first {
                Button("Editar…") { editando = c }
                Divider()
            }
            if !elegidos.isEmpty {
                Button("Eliminar…", role: .destructive) { aEliminar = elegidos }
            }
        } primaryAction: { ids in
            editando = clientes.first { ids.contains($0.id) }
        }
        .overlay {
            if clientes.isEmpty {
                ContentUnavailableView("Sin clientes", systemImage: "person.2",
                                       description: Text("Da de alta las empresas o personas a las que facturas."))
            }
        }
        .navigationTitle("Clientes")
        .toolbar {
            Button { exportando = true } label: { Label("Exportar para la AEAT", systemImage: "square.and.arrow.up") }
                .help("Fichero para Otros servicios → Clientes → Importar en la app de la AEAT")
                .disabled(clientes.isEmpty)
            Button { creando = true } label: { Label("Nuevo cliente", systemImage: "plus") }
        }
        .fileExporter(isPresented: $exportando,
                      document: DocumentoTexto(texto: ExportacionAEAT.clientes(clientes.map(\.paraAEAT))),
                      contentType: .plainText, defaultFilename: "clientes_aeat.txt") { _ in }
        .sheet(item: $editando) { ClienteEditor(cliente: $0) }
        .sheet(isPresented: $creando) { ClienteEditor() }
        .confirmationDialog("¿Eliminar \(aEliminar.count) cliente(s)?", isPresented: .init(
            get: { !aEliminar.isEmpty }, set: { if !$0 { aEliminar = [] } }
        )) {
            Button("Eliminar", role: .destructive) {
                aEliminar.forEach(contexto.delete)
                try? contexto.save()
                aEliminar = []
            }
        } message: {
            Text("Sus ingresos se conservan, pero quedarán sin cliente asignado.")
        }
    }
}

struct ClienteEditor: View {
    @Environment(\.modelContext) private var contexto

    private let cliente: Cliente?
    @State private var nombre: String
    @State private var nif: String
    @State private var direccion: String
    @State private var codigoPostal: String
    @State private var poblacion: String
    @State private var provincia: String
    @State private var pais: String
    @State private var email: String
    @State private var telefono: String
    @State private var notas: String

    init(cliente: Cliente? = nil) {
        self.cliente = cliente
        _nombre = State(initialValue: cliente?.nombre ?? "")
        _nif = State(initialValue: cliente?.nif ?? "")
        _direccion = State(initialValue: cliente?.direccion ?? "")
        _codigoPostal = State(initialValue: cliente?.codigoPostal ?? "")
        _poblacion = State(initialValue: cliente?.poblacion ?? "")
        _provincia = State(initialValue: cliente?.provincia ?? "")
        _pais = State(initialValue: cliente?.pais ?? "ES")
        _email = State(initialValue: cliente?.email ?? "")
        _telefono = State(initialValue: cliente?.telefono ?? "")
        _notas = State(initialValue: cliente?.notas ?? "")
    }

    var body: some View {
        VStack(spacing: 0) {
            Form {
                TextField("Nombre o razón social", text: $nombre)
                TextField("NIF / CIF", text: $nif)
                TextField("Dirección", text: $direccion, prompt: Text("C/ Mayor, 1"))
                TextField("Código postal", text: $codigoPostal)
                TextField("Población", text: $poblacion)
                TextField("Provincia", text: $provincia)
                TextField("País (código)", text: $pais)
                TextField("Email", text: $email)
                TextField("Teléfono", text: $telefono)
                TextField("Notas", text: $notas, axis: .vertical).lineLimit(2...4)
            }
            .formStyle(.grouped)

            BotoneraEditor(puedeGuardar: !nombre.trimmingCharacters(in: .whitespaces).isEmpty, guardar: guardar)
        }
        .frame(width: 460, height: 520)
    }

    private func guardar() {
        let destino = cliente ?? {
            let nuevo = Cliente()
            contexto.insert(nuevo)
            return nuevo
        }()
        destino.nombre = nombre.trimmingCharacters(in: .whitespaces)
        destino.nif = nif.uppercased().replacingOccurrences(of: " ", with: "")
        destino.direccion = direccion
        destino.codigoPostal = codigoPostal
        destino.poblacion = poblacion
        destino.provincia = provincia
        destino.pais = pais.uppercased()
        destino.email = email
        destino.telefono = telefono
        destino.notas = notas
        try? contexto.save()
    }
}
