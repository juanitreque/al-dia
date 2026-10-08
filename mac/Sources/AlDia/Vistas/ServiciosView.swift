import SwiftData
import SwiftUI
import AlDiaCore

struct ServiciosView: View {
    @Environment(\.modelContext) private var contexto
    @Query(sort: \Servicio.codigo) private var servicios: [Servicio]

    @State private var seleccion = Set<PersistentIdentifier>()
    @State private var editando: Servicio?
    @State private var creando = false
    @State private var aEliminar: [Servicio] = []
    @State private var exportando = false

    var body: some View {
        Table(servicios, selection: $seleccion) {
            TableColumn("Código") { Text($0.codigo).monospaced() }.width(120)
            TableColumn("Descripción") { Text($0.descripcion) }
            TableColumn("Precio") { CeldaImporte(valor: $0.precio) }.width(90)
            TableColumn("IVA") { Text($0.tipoIVA.porcentaje) }.width(60)
        }
        .contextMenu(forSelectionType: PersistentIdentifier.self) { ids in
            let elegidos = servicios.filter { ids.contains($0.id) }
            if elegidos.count == 1, let s = elegidos.first {
                Button("Editar…") { editando = s }
                Divider()
            }
            if !elegidos.isEmpty {
                Button("Eliminar…", role: .destructive) { aEliminar = elegidos }
            }
        } primaryAction: { ids in
            editando = servicios.first { ids.contains($0.id) }
        }
        .overlay {
            if servicios.isEmpty {
                ContentUnavailableView("Sin servicios", systemImage: "tag",
                                       description: Text("Los conceptos que facturas (p. ej. una hora de consultoría a 40 €). Se crean solos al importar facturas."))
            }
        }
        .navigationTitle("Servicios")
        .toolbar {
            Button { exportando = true } label: { Label("Exportar para la AEAT", systemImage: "square.and.arrow.up") }
                .help("Fichero para Otros servicios → Productos → Importar en la app de la AEAT")
                .disabled(servicios.isEmpty)
            Button { creando = true } label: { Label("Nuevo servicio", systemImage: "plus") }
        }
        .sheet(item: $editando) { ServicioEditor(servicio: $0) }
        .sheet(isPresented: $creando) { ServicioEditor() }
        .confirmationDialog("¿Eliminar \(aEliminar.count) servicio(s)?", isPresented: .init(
            get: { !aEliminar.isEmpty }, set: { if !$0 { aEliminar = [] } }
        )) {
            Button("Eliminar", role: .destructive) {
                aEliminar.forEach(contexto.delete)
                try? contexto.save()
                aEliminar = []
            }
        } message: {
            Text("Las facturas ya hechas conservan sus líneas.")
        }
        .fileExporter(isPresented: $exportando,
                      document: DocumentoTexto(texto: ExportacionAEAT.productos(servicios.map(\.paraAEAT))),
                      contentType: .plainText, defaultFilename: "productos_aeat.txt") { _ in }
    }
}

struct ServicioEditor: View {
    @Environment(\.modelContext) private var contexto

    private let servicio: Servicio?
    @State private var codigo: String
    @State private var descripcion: String
    @State private var precio: Decimal
    @State private var tipoIVA: Decimal

    init(servicio: Servicio? = nil) {
        self.servicio = servicio
        _codigo = State(initialValue: servicio?.codigo ?? "")
        _descripcion = State(initialValue: servicio?.descripcion ?? "")
        _precio = State(initialValue: servicio?.precio ?? 0)
        _tipoIVA = State(initialValue: servicio?.tipoIVA ?? Ajustes.ivaPorDefecto)
    }

    var body: some View {
        VStack(spacing: 0) {
            Form {
                Section {
                    TextField("Código", text: $codigo, prompt: Text("CONSULTORIA"))
                    TextField("Descripción", text: $descripcion, prompt: Text("Hora de consultoría"))
                    CampoImporte(titulo: "Precio unitario (sin IVA)", valor: $precio)
                    SelectorTipo(titulo: "IVA", opciones: Tipos.iva, valor: $tipoIVA)
                } footer: {
                    Text("El código es el ID en la app de la AEAT: sin espacios.").foregroundStyle(.secondary)
                }
            }
            .formStyle(.grouped)

            BotoneraEditor(puedeGuardar: !codigo.trimmingCharacters(in: .whitespaces).isEmpty
                           && !descripcion.trimmingCharacters(in: .whitespaces).isEmpty,
                           guardar: guardar)
        }
        .frame(width: 460, height: 320)
    }

    private func guardar() {
        let destino = servicio ?? {
            let nuevo = Servicio()
            contexto.insert(nuevo)
            return nuevo
        }()
        destino.codigo = ExportacionAEAT.identificador(codigo)
        destino.descripcion = descripcion.trimmingCharacters(in: .whitespaces)
        destino.precio = precio
        destino.tipoIVA = tipoIVA
        try? contexto.save()
    }
}
