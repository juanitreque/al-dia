import PDFKit
import SwiftData
import SwiftUI
import AlDiaCore

extension View {
    /// Selector de PDF + hoja de revisión para importar facturas ya emitidas.
    func importacionDeFacturas(isPresented: Binding<Bool>) -> some View {
        modifier(ImportacionFacturas(eligiendo: isPresented))
    }
}

private struct ImportacionFacturas: ViewModifier {
    @Binding var eligiendo: Bool
    @State private var lote: LoteImportacion?

    func body(content: Content) -> some View {
        content
            .fileImporter(isPresented: $eligiendo, allowedContentTypes: [.pdf], allowsMultipleSelection: true) { resultado in
                guard case .success(let urls) = resultado, !urls.isEmpty else { return }
                lote = LoteImportacion(filas: urls.sorted { $0.lastPathComponent < $1.lastPathComponent }.map(FilaImportacion.leer))
            }
            .sheet(item: $lote) { ImportacionView(filas: $0.filas) }
    }
}

private struct LoteImportacion: Identifiable {
    let id = UUID()
    let filas: [FilaImportacion]
}

struct FilaImportacion: Identifiable {
    let id = UUID()
    let archivo: String
    let datos: Data
    let factura: FacturaLeida?

    static func leer(_ url: URL) -> FilaImportacion {
        let acceso = url.startAccessingSecurityScopedResource()
        defer { if acceso { url.stopAccessingSecurityScopedResource() } }
        let datos = (try? Data(contentsOf: url)) ?? Data()
        let texto = PDFDocument(data: datos)?.string ?? ""
        return FilaImportacion(archivo: url.lastPathComponent, datos: datos, factura: LectorFactura.analizar(texto))
    }
}

struct ImportacionView: View {
    @Environment(\.modelContext) private var contexto
    @Environment(\.dismiss) private var dismiss
    @Query private var ingresos: [Ingreso]
    @Query private var clientes: [Cliente]
    @Query private var servicios: [Servicio]

    let filas: [FilaImportacion]
    @State private var marcarCobradas = true

    private var numerosExistentes: Set<String> { Set(ingresos.map(\.numero)) }

    private func estado(_ fila: FilaImportacion) -> (texto: String, color: Color, importable: Bool) {
        guard let f = fila.factura else { return (String(localized: "No reconocida"), .red, false) }
        if numerosExistentes.contains(f.numero) { return (String(localized: "Ya existe"), .secondary, false) }
        if !f.avisos.isEmpty { return (f.avisos.map(traducido).joined(separator: "; "), .orange, true) }
        return (String(localized: "Correcta"), .green, true)
    }

    var body: some View {
        let importables = filas.filter { estado($0).importable }
        VStack(alignment: .leading, spacing: 0) {
            VStack(alignment: .leading, spacing: 4) {
                Text("Importar facturas").font(.title3.bold())
                Text("Revisa los datos leídos de cada PDF. Se importan como facturas emitidas, con el PDF adjunto.")
                    .foregroundStyle(.secondary)
            }
            .padding()

            Table(filas) {
                TableColumn("Archivo") { Text($0.archivo).lineLimit(1) }
                TableColumn("Nº") { Text($0.factura?.numero ?? "—") }.width(70)
                TableColumn("Fecha") { Text($0.factura?.fecha.corta ?? "—").monospacedDigit() }.width(85)
                TableColumn("Cliente") { Text($0.factura?.clienteNombre ?? "—").lineLimit(1) }
                TableColumn("Base") { CeldaImporte(valor: $0.factura?.base ?? 0) }.width(80)
                TableColumn("IVA") { CeldaImporte(valor: $0.factura?.cuotaIVA ?? 0) }.width(75)
                TableColumn("IRPF") { CeldaImporte(valor: $0.factura?.retencion ?? 0) }.width(75)
                TableColumn("Total") { CeldaImporte(valor: $0.factura?.total ?? 0) }.width(85)
                TableColumn("Estado") { fila in
                    let e = estado(fila)
                    Text(e.texto).foregroundStyle(e.color).lineLimit(2).help(e.texto)
                }
            }

            HStack {
                Toggle("Marcarlas como cobradas", isOn: $marcarCobradas)
                Spacer()
                Text("Base total: \(importables.compactMap(\.factura).suma(\.base).euros)").foregroundStyle(.secondary)
                Button("Cancelar") { dismiss() }.keyboardShortcut(.cancelAction)
                Button("Importar \(importables.count)") { importar(importables) }
                    .keyboardShortcut(.defaultAction)
                    .disabled(importables.isEmpty)
            }
            .padding()
        }
        .frame(width: 980, height: 520)
    }

    private func importar(_ seleccion: [FilaImportacion]) {
        var clientesPorNIF = Dictionary(clientes.map { ($0.nif.uppercased(), $0) }, uniquingKeysWith: { a, _ in a })
        var serviciosPorCodigo = Dictionary(servicios.map { ($0.codigo, $0) }, uniquingKeysWith: { a, _ in a })

        for fila in seleccion {
            guard let f = fila.factura else { continue }

            let nif = f.clienteNIF.uppercased()
            let cliente = clientesPorNIF[nif] ?? {
                let nuevo = Cliente(nombre: f.clienteNombre)
                nuevo.nif = nif
                contexto.insert(nuevo)
                clientesPorNIF[nif] = nuevo
                return nuevo
            }()

            for l in f.lineas {
                let codigo = ExportacionAEAT.identificador(l.concepto)
                if let existente = serviciosPorCodigo[codigo] {
                    existente.precio = l.precio  // queda el precio más reciente
                } else {
                    let nuevo = Servicio(codigo: codigo, descripcion: l.concepto, precio: l.precio, tipoIVA: f.tipoIVA)
                    contexto.insert(nuevo)
                    serviciosPorCodigo[codigo] = nuevo
                }
            }

            let ingreso = Ingreso()
            contexto.insert(ingreso)
            ingreso.estado = .emitida
            ingreso.numero = f.numero
            ingreso.fecha = f.fecha
            ingreso.cliente = cliente
            ingreso.concepto = conceptoSugerido(f.fecha)
            ingreso.base = f.base
            ingreso.tipoIVA = f.tipoIVA
            ingreso.cuotaIVA = f.cuotaIVA
            ingreso.tipoRetencion = f.tipoRetencion
            ingreso.retencion = f.retencion
            ingreso.cobrada = marcarCobradas
            ingreso.adjunto = fila.datos
            ingreso.adjuntoNombre = fila.archivo
            ingreso.lineas = f.lineas.enumerated().map { orden, l in
                LineaIngreso(orden: orden, codigo: ExportacionAEAT.identificador(l.concepto), concepto: l.concepto,
                             cantidad: l.cantidad, precio: l.precio)
            }
        }
        try? contexto.save()
        dismiss()
    }
}
