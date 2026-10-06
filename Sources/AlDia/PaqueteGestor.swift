import AppKit
import SwiftData
import SwiftUI
import AlDiaCore

// MARK: - Libros en CSV (Excel en español)

enum Libros {
    static func ingresos(_ lista: [Ingreso]) -> DocumentoCSV {
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

    static func gastos(_ lista: [Gasto]) -> DocumentoCSV {
        var filas = [["Fecha", "Nº factura", "NIF proveedor", "Proveedor", "Concepto", "Categoría",
                      "Base imponible", "Tipo IVA", "Cuota IVA", "% deducible", "IVA deducible",
                      "Gasto IRPF", "Factura completa", "Bien de inversión"]]
        for g in lista.sorted(by: { $0.fecha < $1.fecha }) {
            let d = g.datos
            filas.append([
                g.fecha.corta, g.numeroFactura, g.nifProveedor, g.proveedor, g.concepto, g.categoria.rawValue,
                DocumentoCSV.importe(g.base), DocumentoCSV.importe(g.tipoIVA), DocumentoCSV.importe(g.cuotaIVA),
                DocumentoCSV.importe(g.porcentajeDeducibleIVA), DocumentoCSV.importe(d.ivaDeducible),
                DocumentoCSV.importe(d.gastoIRPF), g.facturaCompleta ? "Sí" : "No", g.bienInversion ? "Sí" : "No",
            ])
        }
        return DocumentoCSV(filas: filas)
    }
}

extension DocumentoCSV {
    /// Contenido del fichero tal como se guarda (UTF-8 con BOM para Excel).
    var datos: Data { Data(("\u{FEFF}" + texto).utf8) }
}

// MARK: - Modelos presentados que afectan al cálculo

extension Array where Element == ModeloPresentado {
    /// Pagos del 130 ya ingresados en trimestres anteriores del mismo ejercicio (casilla 05).
    func pagos130Anteriores(a trimestre: Trimestre) -> Decimal {
        filter {
            $0.tipo == .m130 && $0.ejercicio == trimestre.ejercicio && $0.tipoResultado == .ingresar
                && $0.periodo < trimestre.periodo
        }.suma(\.importe)
    }

    /// IVA que quedó a compensar en el 303 del trimestre anterior.
    func pendienteCompensar303(en trimestre: Trimestre) -> Decimal {
        let anterior = trimestre.anterior
        return first { $0.corresponde(a: .m303, anterior) && $0.tipoResultado == .compensar }?.importe ?? 0
    }
}

// MARK: - Paquete

/// Zip trimestral para la gestoría: resumen, libros en CSV, PDF de las facturas,
/// documentos de los gastos y justificantes de los modelos presentados.
struct PaqueteGestor {
    let trimestre: Trimestre
    let ingresos: [Ingreso]   // emitidas del trimestre
    let gastos: [Gasto]       // del trimestre
    let modelos: [ModeloPresentado]
    let todosIngresos: [Ingreso]
    let todosGastos: [Gasto]

    init(trimestre: Trimestre, ingresos: [Ingreso], gastos: [Gasto], modelos: [ModeloPresentado]) {
        self.trimestre = trimestre
        todosIngresos = ingresos.emitidas
        todosGastos = gastos
        self.ingresos = todosIngresos.filter { trimestre.contiene($0.fecha) }.sorted { $0.fecha < $1.fecha }
        self.gastos = gastos.filter { trimestre.contiene($0.fecha) }.sorted { $0.fecha < $1.fecha }
        self.modelos = modelos
    }

    /// "Nombre Apellido - 3T 2026": el gestor reconoce de quién es y de qué periodo.
    var nombre: String {
        let autonomo = Ajustes.texto(Ajustes.nombre)
        return autonomo.isEmpty
            ? String(localized: "Documentación \(trimestre.nombre)")
            : "\(autonomo) - \(trimestre.nombre)"
    }

    var presentados: [ModeloPresentado] {
        modelos.filter { $0.ejercicio == trimestre.ejercicio && $0.periodo == trimestre.periodo }
    }

    var gastosSinDocumento: [Gasto] { gastos.filter { ($0.adjunto ?? Data()).isEmpty } }

    /// Crea el zip en una carpeta temporal y devuelve su URL.
    @MainActor
    func crear(incluirDocumentos: Bool) throws -> URL {
        let fm = FileManager.default
        let base = fm.temporaryDirectory.appendingPathComponent(UUID().uuidString, isDirectory: true)
        let carpeta = base.appendingPathComponent(limpio(nombre), isDirectory: true)
        try fm.createDirectory(at: carpeta, withIntermediateDirectories: true)

        try Data(resumen().utf8).write(to: carpeta.appendingPathComponent(limpio(String(localized: "Resumen \(trimestre.nombre).txt"))))
        try Libros.ingresos(ingresos).datos.write(to: carpeta.appendingPathComponent(limpio(String(localized: "Ingresos \(trimestre.nombre).csv"))))
        try Libros.gastos(gastos).datos.write(to: carpeta.appendingPathComponent(limpio(String(localized: "Gastos \(trimestre.nombre).csv"))))

        if incluirDocumentos {
            let facturas = carpeta.appendingPathComponent(String(localized: "Facturas emitidas"), isDirectory: true)
            try fm.createDirectory(at: facturas, withIntermediateDirectories: true)
            for i in ingresos {
                // El documento guardado (PDF original o el de Al Día); si no hay, se genera el de Al Día
                let datos = (i.adjunto ?? Data()).isEmpty ? FacturaPDF.generar(i) : i.adjunto!
                let ext = ((i.adjuntoNombre ?? "") as NSString).pathExtension.lowercased()
                let nombre = String(localized: "Factura \(i.numero)") + ".\((i.adjunto ?? Data()).isEmpty || ext.isEmpty ? "pdf" : ext)"
                try datos.write(to: facturas.appendingPathComponent(limpio(nombre)))
            }

            let documentosGastos = carpeta.appendingPathComponent(String(localized: "Gastos"), isDirectory: true)
            if gastos.contains(where: { !($0.adjunto ?? Data()).isEmpty }) {
                try fm.createDirectory(at: documentosGastos, withIntermediateDirectories: true)
            }
            for g in gastos {
                guard let datos = g.adjunto, !datos.isEmpty else { continue }
                let ext = ((g.adjuntoNombre ?? "") as NSString).pathExtension
                let fecha = g.fecha.formatted(.iso8601.year().month().day())
                let partes = [fecha, g.proveedor, g.numeroFactura].filter { !$0.isEmpty }
                let nombre = partes.joined(separator: " - ") + (ext.isEmpty ? "" : ".\(ext)")
                try datos.write(to: sinRepetir(documentosGastos.appendingPathComponent(limpio(nombre))))
            }

            let justificantes = presentados.filter { !($0.adjunto ?? Data()).isEmpty }
            if !justificantes.isEmpty {
                let carpetaModelos = carpeta.appendingPathComponent(String(localized: "Modelos presentados"), isDirectory: true)
                try fm.createDirectory(at: carpetaModelos, withIntermediateDirectories: true)
                for m in justificantes {
                    let ext = ((m.adjuntoNombre ?? "") as NSString).pathExtension
                    let nombre = "Modelo \(m.modelo) \(m.periodo) \(m.ejercicio)" + (ext.isEmpty ? "" : ".\(ext)")
                    try m.adjunto!.write(to: sinRepetir(carpetaModelos.appendingPathComponent(limpio(nombre))))
                }
            }
        }

        // Comprimir con ditto (como «Comprimir» del Finder)
        let zip = base.appendingPathComponent(limpio(nombre) + ".zip")
        let proceso = Process()
        proceso.executableURL = URL(fileURLWithPath: "/usr/bin/ditto")
        // Sin datos internos del Mac (__MACOSX), para que en Windows solo se vean los documentos
        proceso.arguments = ["-c", "-k", "--norsrc", "--noextattr", "--keepParent", carpeta.path, zip.path]
        try proceso.run()
        proceso.waitUntilExit()
        guard proceso.terminationStatus == 0 else { throw CocoaError(.fileWriteUnknown) }
        return zip
    }

    /// Resumen en texto: totales, 303 y 130 con casillas, y avisos.
    func resumen() -> String {
        let emisor = Emisor()
        let datosIngresos = todosIngresos.map(\.datos)
        let datosGastos = todosGastos.map(\.datos)
        let l303 = Liquidacion303(trimestre: trimestre, ingresos: datosIngresos, gastos: datosGastos)
        let l130 = Liquidacion130(trimestre: trimestre, ingresos: datosIngresos, gastos: datosGastos,
                                  pagosAnteriores: modelos.pagos130Anteriores(a: trimestre))
        let pendiente = modelos.pendienteCompensar303(en: trimestre)
        let dg = gastos.map(\.datos)
        let rango = "\(trimestre.inicio.corta) – \(Calendar.fiscal.date(byAdding: .day, value: -1, to: trimestre.fin)!.corta)"

        var t = ""
        func linea(_ texto: String = "") { t += texto + "\n" }
        func cifra(_ titulo: String, _ valor: Decimal) {
            let izquierda = "  " + titulo
            let importe = valor.euros
            let relleno = max(2, 64 - izquierda.count - importe.count)
            linea(izquierda + String(repeating: " ", count: relleno) + importe)
        }

        linea(String(localized: "RESUMEN \(trimestre.nombre) (\(rango))"))
        linea("\(emisor.nombre) · NIF \(emisor.nif)")
        linea(String(localized: "Preparado el \(Date.now.corta). Cifras orientativas: revisar antes de presentar."))
        linea()
        linea(String(localized: "INGRESOS (facturas emitidas): \(ingresos.count)"))
        cifra(String(localized: "Base imponible"), ingresos.suma(\.base))
        cifra(String(localized: "IVA repercutido"), ingresos.suma(\.cuotaIVA))
        cifra(String(localized: "Retenciones IRPF"), ingresos.suma(\.retencion))
        cifra(String(localized: "Total facturado"), ingresos.suma(\.total))
        linea()
        linea(String(localized: "GASTOS: \(gastos.count)"))
        cifra(String(localized: "Base imponible"), gastos.suma(\.base))
        cifra(String(localized: "IVA soportado"), gastos.suma(\.cuotaIVA))
        cifra(String(localized: "IVA deducible"), dg.suma(\.ivaDeducible))
        cifra(String(localized: "Gasto deducible IRPF"), dg.suma(\.gastoIRPF))
        linea()
        linea(String(localized: "MODELO 303 · IVA"))
        for d in l303.devengos {
            if let c = d.casillas {
                cifra("[\(c.base)] " + String(localized: "Base imponible al \(d.tipo.porcentaje)"), d.base)
                cifra("[\(c.cuota)] " + String(localized: "Cuota devengada al \(d.tipo.porcentaje)"), d.cuota)
            }
        }
        cifra("[27] " + String(localized: "Total cuota devengada"), l303.totalDevengado)
        cifra("[28] " + String(localized: "Base operaciones interiores corrientes"), l303.baseCorrientes)
        cifra("[29] " + String(localized: "Cuota deducible operaciones corrientes"), l303.cuotaCorrientes)
        if l303.cuotaInversion > 0 {
            cifra("[30] " + String(localized: "Base bienes de inversión"), l303.baseInversion)
            cifra("[31] " + String(localized: "Cuota deducible bienes de inversión"), l303.cuotaInversion)
        }
        cifra("[45] " + String(localized: "Total a deducir"), l303.totalDeducir)
        cifra("[46] " + String(localized: "Resultado régimen general"), l303.resultado)
        if pendiente > 0 {
            cifra(String(localized: "Cuotas a compensar de periodos anteriores"), -pendiente)
            cifra(String(localized: "Resultado de la liquidación"), l303.resultado(compensando: pendiente))
        }
        linea()
        linea(String(localized: "MODELO 130 · Pago fraccionado IRPF"))
        cifra("[01] " + String(localized: "Ingresos computables (1 ene – fin del trimestre)"), l130.ingresos)
        cifra("[02] " + String(localized: "Gastos fiscalmente deducibles"), l130.gastos)
        cifra("[03] " + String(localized: "Rendimiento neto"), l130.rendimiento)
        cifra("[04] " + String(localized: "20 % del rendimiento"), l130.cuota)
        cifra("[05] " + String(localized: "Pagos fraccionados de trimestres anteriores"), -l130.pagosAnteriores)
        cifra("[06] " + String(localized: "Retenciones soportadas"), -l130.retenciones)
        cifra("[07] " + String(localized: "Pago fraccionado previo"), l130.resultado)
        if let pct = Liquidacion130.porcentajeConRetencion(datosIngresos, ejercicio: trimestre.ejercicio - 1)
            ?? Liquidacion130.porcentajeConRetencion(datosIngresos, ejercicio: trimestre.ejercicio), pct >= 70 {
            linea("  " + String(localized: "\(pct.porcentaje) de ingresos con retención: no estás obligado a presentar el 130."))
        }
        if !gastosSinDocumento.isEmpty {
            linea()
            linea(String(localized: "GASTOS SIN DOCUMENTO ADJUNTO: \(gastosSinDocumento.count)"))
            for g in gastosSinDocumento { linea("  \(g.fecha.corta) · \(g.proveedor) · \(g.total.euros)") }
        }
        if !presentados.isEmpty {
            linea()
            linea(String(localized: "MODELOS PRESENTADOS"))
            for m in presentados {
                linea("  \(m.modelo) · \(m.fechaPresentacion.corta) · \(m.tipoResultado.nombre) \(m.importe.euros) · \(m.justificante)")
            }
        }
        return t
    }

    /// Nombre de archivo seguro dentro del zip: sin barras ni tildes (algunos programas de
    /// Windows muestran mal los nombres con tildes de los zip hechos en Mac).
    private func limpio(_ nombre: String) -> String {
        nombre.replacingOccurrences(of: "/", with: "-").replacingOccurrences(of: ":", with: "-")
            .folding(options: [.diacriticInsensitive], locale: Locale(identifier: "es_ES"))
    }

    private func sinRepetir(_ url: URL) -> URL {
        var candidato = url
        var n = 2
        while FileManager.default.fileExists(atPath: candidato.path) {
            let ext = url.pathExtension
            let base = url.deletingPathExtension().lastPathComponent
            candidato = url.deletingLastPathComponent().appendingPathComponent("\(base) (\(n))" + (ext.isEmpty ? "" : ".\(ext)"))
            n += 1
        }
        return candidato
    }
}

// MARK: - Hoja

struct PaqueteGestorView: View {
    @Environment(\.dismiss) private var dismiss
    @Query private var ingresos: [Ingreso]
    @Query private var gastos: [Gasto]
    @Query private var modelos: [ModeloPresentado]
    @AppStorage(Ajustes.emailGestor) private var emailGestor = ""

    let trimestre: Trimestre
    @State private var incluirDocumentos = true
    @State private var error: String?

    private var paquete: PaqueteGestor {
        PaqueteGestor(trimestre: trimestre, ingresos: ingresos, gastos: gastos, modelos: modelos)
    }

    var body: some View {
        let p = paquete
        VStack(spacing: 0) {
            Form {
                Section {
                    LabeledContent("Facturas emitidas", value: "\(p.ingresos.count)")
                    LabeledContent("Gastos", value: "\(p.gastos.count)")
                    LabeledContent("Modelos presentados", value: "\(p.presentados.count)")
                    Toggle("Incluir los PDF de facturas, tickets y justificantes", isOn: $incluirDocumentos)
                    TextField("Email del gestor", text: $emailGestor, prompt: Text("gestoria@ejemplo.es"))
                } header: {
                    Text("Paquete para el gestor · \(trimestre.nombre)")
                } footer: {
                    Text("Incluye un resumen con el 303 y el 130, los libros de ingresos y gastos en CSV y, si lo marcas, todos los documentos.")
                        .foregroundStyle(.secondary)
                }
                if !p.gastosSinDocumento.isEmpty {
                    Label("\(p.gastosSinDocumento.count) gasto(s) sin documento adjunto: aparecen en el resumen para que el gestor lo sepa.",
                          systemImage: "exclamationmark.triangle")
                        .foregroundStyle(.orange)
                }
                if p.ingresos.isEmpty && p.gastos.isEmpty {
                    Label("No hay facturas emitidas ni gastos en este trimestre.", systemImage: "info.circle")
                        .foregroundStyle(.secondary)
                }
                if let error {
                    Text(verbatim: error).foregroundStyle(.red)
                }
            }
            .formStyle(.grouped)

            HStack {
                Spacer()
                Button("Cerrar") { dismiss() }.keyboardShortcut(.cancelAction)
                Button("Guardar ZIP…") { guardar() }
                Button("Enviar por correo…") { enviar() }.keyboardShortcut(.defaultAction)
            }
            .disabled(p.ingresos.isEmpty && p.gastos.isEmpty)
            .padding()
        }
        .frame(width: 520, height: 470)
    }

    private func crear() -> URL? {
        do {
            error = nil
            return try paquete.crear(incluirDocumentos: incluirDocumentos)
        } catch {
            self.error = String(localized: "No se pudo crear el paquete: \(error.localizedDescription)")
            return nil
        }
    }

    private func guardar() {
        guard let zip = crear() else { return }
        let panel = NSSavePanel()
        panel.allowedContentTypes = [.zip]
        panel.nameFieldStringValue = zip.lastPathComponent
        if panel.runModal() == .OK, let destino = panel.url {
            try? FileManager.default.removeItem(at: destino)
            do { try FileManager.default.copyItem(at: zip, to: destino) } catch { self.error = error.localizedDescription }
        }
    }

    private func enviar() {
        guard let zip = crear() else { return }
        let p = paquete
        let cuerpo = String(localized: """
        Hola:

        Te envío la documentación del \(trimestre.nombre): \(p.ingresos.count) facturas emitidas y \(p.gastos.count) gastos, \
        con el resumen del 303 y el 130.

        Un saludo,
        \(Ajustes.texto(Ajustes.nombre))
        """)
        let autonomo = Ajustes.texto(Ajustes.nombre)
        let asunto = String(localized: "Documentación \(trimestre.nombre)") + (autonomo.isEmpty ? "" : " · \(autonomo)")
        Correo.enviar(adjunto: zip, para: emailGestor, asunto: asunto, cuerpo: cuerpo)
    }
}

// MARK: - Correo

enum Correo {
    /// Correo nuevo en la app de correo con un adjunto; si no se puede, muestra el archivo en el Finder.
    @MainActor
    static func enviar(adjunto: URL, para: String, asunto: String, cuerpo: String) {
        guard let servicio = NSSharingService(named: .composeEmail) else {
            NSWorkspace.shared.activateFileViewerSelecting([adjunto])
            return
        }
        servicio.subject = asunto
        let destinatario = para.trimmingCharacters(in: .whitespaces)
        if !destinatario.isEmpty { servicio.recipients = [destinatario] }
        if servicio.canPerform(withItems: [cuerpo, adjunto]) {
            servicio.perform(withItems: [cuerpo, adjunto])
        } else {
            NSWorkspace.shared.activateFileViewerSelecting([adjunto])
        }
    }
}
