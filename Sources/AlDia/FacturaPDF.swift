import AppKit
import PDFKit
import SwiftData
import SwiftUI
import AlDiaCore

/// Fecha en que la factura con programas debe cumplir VeriFactu para autónomos (RD 1007/2023, RDL 15/2025).
/// Hacienda anunció en octubre de 2026 un aplazamiento a octubre de 2028, pendiente de publicarse en el BOE:
/// cuando se publique, basta con cambiar esta fecha.
enum VeriFactu {
    static let obligatorioDesde = Calendar.fiscal.date(from: DateComponents(year: 2027, month: 7, day: 1))!

    static func obliga(en fecha: Date) -> Bool { fecha >= obligatorioDesde }
}

/// Datos del emisor tomados de Ajustes.
struct Emisor {
    var nombre = Ajustes.texto(Ajustes.nombre)
    var nif = Ajustes.texto(Ajustes.nif)
    var direccion = Ajustes.texto(Ajustes.direccion)
    var codigoPostal = Ajustes.texto(Ajustes.codigoPostal)
    var poblacion = Ajustes.texto(Ajustes.poblacion)
    var provincia = Ajustes.texto(Ajustes.provincia)
    var email = Ajustes.texto(Ajustes.email)
    var telefono = Ajustes.texto(Ajustes.telefono)
    var iban = Ajustes.texto(Ajustes.iban)
    var pie = Ajustes.texto(Ajustes.pieFactura)

    /// "29600 Marbella (Málaga)": la provincia solo si no coincide con la población.
    var localidad: String {
        let base = [codigoPostal, poblacion].filter { !$0.isEmpty }.joined(separator: " ")
        guard !provincia.isEmpty, provincia.lowercased() != poblacion.lowercased() else { return base }
        return "\(base) (\(provincia))"
    }

    /// Pie con los marcadores {nombre}, {nif}, {domicilio} y {email} sustituidos.
    var pieFinal: String {
        PieFactura.rellenar(pie, nombre: nombre, nif: nif,
                            domicilio: [direccion, localidad].filter { !$0.isEmpty }.joined(separator: ", "), email: email)
    }
}

/// Lo que falta para que la factura tenga los datos obligatorios (art. 6 del Reglamento de facturación).
func datosQueFaltan(_ ingreso: Ingreso, emisor: Emisor) -> [String] {
    var faltan: [String] = []
    if emisor.nombre.isEmpty { faltan.append(String(localized: "Tu nombre (Ajustes)")) }
    if emisor.nif.isEmpty { faltan.append(String(localized: "Tu NIF (Ajustes)")) }
    if emisor.direccion.isEmpty || emisor.poblacion.isEmpty {
        faltan.append(String(localized: "Tu domicilio fiscal (Ajustes)"))
    }
    if let c = ingreso.cliente {
        if c.nif.isEmpty { faltan.append(String(localized: "NIF del cliente (Clientes)")) }
        if c.direccion.isEmpty || c.poblacion.isEmpty { faltan.append(String(localized: "Domicilio del cliente (Clientes)")) }
    } else {
        faltan.append(String(localized: "Cliente de la factura"))
    }
    if ingreso.numero.trimmingCharacters(in: .whitespaces).isEmpty { faltan.append(String(localized: "Número de factura")) }
    return faltan
}

// MARK: - Plantilla

/// Factura en A4 (595 × 842 puntos), siempre en claro.
struct PlantillaFactura: View {
    let ingreso: Ingreso
    let emisor: Emisor
    /// Marca de agua «BORRADOR» para la vista previa de una factura aún no emitida.
    var borrador = false

    private let gris = Color(white: 0.4)
    private let linea = Color(white: 0.85)

    private var lineas: [(concepto: String, cantidad: Decimal, precio: Decimal, importe: Decimal)] {
        let guardadas = ingreso.lineasOrdenadas
        if guardadas.isEmpty { return [(ingreso.concepto, 1, ingreso.base, ingreso.base)] }
        return guardadas.map { ($0.concepto, $0.cantidad, $0.precio, $0.importe) }
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 26) {
            HStack(alignment: .top) {
                VStack(alignment: .leading, spacing: 3) {
                    Text(verbatim: emisor.nombre).font(.system(size: 17, weight: .semibold))
                    Text(verbatim: "NIF \(emisor.nif)")
                    Text(verbatim: emisor.direccion)
                    Text(verbatim: localidad(emisor.codigoPostal, emisor.poblacion, emisor.provincia))
                    if !emisor.email.isEmpty || !emisor.telefono.isEmpty {
                        Text(verbatim: [emisor.email, emisor.telefono].filter { !$0.isEmpty }.joined(separator: " · "))
                            .foregroundStyle(gris)
                    }
                }
                Spacer()
                VStack(alignment: .trailing, spacing: 3) {
                    Text("FACTURA").font(.system(size: 26, weight: .bold)).kerning(1)
                    Text("Nº \(ingreso.numero)").font(.system(size: 13, weight: .semibold))
                    Text("Fecha: \(ingreso.fecha.corta)")
                }
            }

            VStack(alignment: .leading, spacing: 3) {
                Text("CLIENTE").font(.system(size: 9, weight: .semibold)).foregroundStyle(gris).kerning(0.8)
                if let c = ingreso.cliente {
                    Text(verbatim: c.nombre).font(.system(size: 12, weight: .semibold))
                    Text(verbatim: "NIF \(c.nif)")
                    Text(verbatim: c.direccion)
                    Text(verbatim: localidad(c.codigoPostal, c.poblacion, c.provincia))
                }
            }
            .padding(12)
            .frame(maxWidth: .infinity, alignment: .leading)
            .overlay(RoundedRectangle(cornerRadius: 6).stroke(linea))

            VStack(spacing: 0) {
                filaTabla(Text("Concepto"), Text("Cantidad"), Text("Precio"), Text("Importe"), cabecera: true)
                Rectangle().fill(Color.black).frame(height: 0.8)
                ForEach(Array(lineas.enumerated()), id: \.offset) { _, l in
                    filaTabla(Text(verbatim: l.concepto), Text(verbatim: l.cantidad.cantidad),
                              Text(verbatim: l.precio.euros), Text(verbatim: l.importe.euros))
                    Rectangle().fill(linea).frame(height: 0.5)
                }
            }

            HStack {
                Spacer()
                VStack(spacing: 5) {
                    total(Text("Base imponible"), ingreso.base)
                    total(Text("IVA \(ingreso.tipoIVA.porcentaje)"), ingreso.cuotaIVA)
                    if ingreso.retencion != 0 {
                        total(Text("Retención IRPF \(ingreso.tipoRetencion.porcentaje)"), -ingreso.retencion)
                    }
                    Rectangle().fill(Color.black).frame(height: 0.8)
                    total(Text("TOTAL A PAGAR"), ingreso.total, destacado: true)
                }
                .frame(width: 240)
            }

            if !emisor.iban.isEmpty {
                VStack(alignment: .leading, spacing: 3) {
                    Text("FORMA DE PAGO").font(.system(size: 9, weight: .semibold)).foregroundStyle(gris).kerning(0.8)
                    Text("Transferencia bancaria a \(IBAN.formatear(emisor.iban))")
                }
            }

            Spacer(minLength: 0)

            // El pie se dibuja justificado al generar el PDF (SwiftUI no justifica texto);
            // aquí solo se le reserva el sitio para que nada se le monte encima.
            if !emisor.pieFinal.isEmpty {
                Color.clear.frame(height: PiePDF.altura(emisor.pieFinal))
            }
        }
        .font(.system(size: 10.5))
        .foregroundStyle(Color.black)
        .padding(48)
        .frame(width: 595, height: 842, alignment: .topLeading)
        .background(Color.white)
        .overlay {
            if borrador {
                Text("BORRADOR")
                    .font(.system(size: 110, weight: .heavy))
                    .foregroundStyle(Color.red.opacity(0.12))
                    .rotationEffect(.degrees(-35))
            }
        }
        .environment(\.colorScheme, .light)
    }

    /// "28001 Madrid" o "29600 Marbella (Málaga)": la provincia solo si no coincide con la población.
    private func localidad(_ cp: String, _ poblacion: String, _ provincia: String) -> String {
        let base = [cp, poblacion].filter { !$0.isEmpty }.joined(separator: " ")
        guard !provincia.isEmpty, provincia.lowercased() != poblacion.lowercased() else { return base }
        return "\(base) (\(provincia))"
    }

    private func filaTabla(_ concepto: Text, _ cantidad: Text, _ precio: Text, _ importe: Text, cabecera: Bool = false) -> some View {
        HStack(alignment: .firstTextBaseline, spacing: 8) {
            concepto.frame(maxWidth: .infinity, alignment: .leading)
            cantidad.frame(width: 60, alignment: .trailing)
            precio.frame(width: 80, alignment: .trailing)
            importe.frame(width: 90, alignment: .trailing)
        }
        .font(.system(size: cabecera ? 9 : 10.5, weight: cabecera ? .semibold : .regular))
        .foregroundStyle(cabecera ? gris : Color.black)
        .padding(.vertical, 6)
    }

    private func total(_ titulo: Text, _ valor: Decimal, destacado: Bool = false) -> some View {
        HStack {
            titulo
            Spacer()
            Text(verbatim: valor.euros).monospacedDigit()
        }
        .font(.system(size: destacado ? 13 : 10.5, weight: destacado ? .bold : .regular))
    }
}

// MARK: - Pie justificado

/// Pie de la factura dibujado con el motor de texto del sistema, que sí justifica
/// (alineado a ambos márgenes, la última línea a la izquierda).
enum PiePDF {
    static let margen: CGFloat = 48
    static let ancho: CGFloat = 595 - 2 * margen

    private static var atributos: [NSAttributedString.Key: Any] {
        let parrafo = NSMutableParagraphStyle()
        parrafo.alignment = .justified
        parrafo.hyphenationFactor = 0.6
        parrafo.lineSpacing = 1
        return [
            .font: NSFont.systemFont(ofSize: 7.5),
            .foregroundColor: NSColor(white: 0.4, alpha: 1),
            .paragraphStyle: parrafo,
        ]
    }

    static func altura(_ texto: String) -> CGFloat {
        guard !texto.isEmpty else { return 0 }
        let caja = NSAttributedString(string: texto, attributes: atributos)
            .boundingRect(with: CGSize(width: ancho, height: .greatestFiniteMagnitude),
                          options: [.usesLineFragmentOrigin, .usesFontLeading])
        return ceil(caja.height)
    }

    /// Dibuja el pie pegado al margen inferior de una página A4 (origen abajo a la izquierda).
    static func dibujar(_ texto: String, en contexto: CGContext) {
        guard !texto.isEmpty else { return }
        NSGraphicsContext.saveGraphicsState()
        NSGraphicsContext.current = NSGraphicsContext(cgContext: contexto, flipped: false)
        let caja = CGRect(x: margen, y: margen, width: ancho, height: altura(texto))
        NSAttributedString(string: texto, attributes: atributos)
            .draw(with: caja, options: [.usesLineFragmentOrigin, .usesFontLeading])
        NSGraphicsContext.restoreGraphicsState()
    }
}

// MARK: - Generación, archivo y correo

enum FacturaPDF {
    /// PDF vectorial (texto seleccionable) de la factura.
    @MainActor
    static func generar(_ ingreso: Ingreso, emisor: Emisor = Emisor(), borrador: Bool = false) -> Data {
        let renderer = ImageRenderer(content: PlantillaFactura(ingreso: ingreso, emisor: emisor, borrador: borrador))
        let datos = NSMutableData()
        renderer.render { tamaño, pintar in
            var caja = CGRect(origin: .zero, size: tamaño)
            guard let consumidor = CGDataConsumer(data: datos),
                  let contexto = CGContext(consumer: consumidor, mediaBox: &caja, nil) else { return }
            contexto.beginPDFPage(nil)
            contexto.saveGState()
            pintar(contexto)
            contexto.restoreGState()
            PiePDF.dibujar(emisor.pieFinal, en: contexto)
            contexto.endPDFPage()
            contexto.closePDF()
        }
        return datos as Data
    }

    /// Diálogo de impresión del sistema para un PDF.
    @MainActor
    static func imprimir(_ datos: Data) {
        guard let documento = PDFDocument(data: datos),
              let operacion = documento.printOperation(for: NSPrintInfo.shared, scalingMode: .pageScaleToFit, autoRotate: true)
        else { NSSound.beep(); return }
        operacion.run()
    }

    static func nombreArchivo(_ ingreso: Ingreso) -> String {
        let numero = ingreso.numero.replacingOccurrences(of: "/", with: "-")
        return String(localized: "Factura \(numero).pdf")
    }

    /// Copia en ~/Documents/Al Día/Facturas/<año>/ (se sobrescribe si ya existía).
    static func guardarCopia(_ datos: Data, de ingreso: Ingreso) throws -> URL {
        let año = String(Calendar.fiscal.component(.year, from: ingreso.fecha))
        let raiz = Demo.activo ? Demo.carpeta : FileManager.default.urls(for: .documentDirectory, in: .userDomainMask)[0]
        let carpeta = raiz
            .appendingPathComponent("Al Día", isDirectory: true)
            .appendingPathComponent(String(localized: "Facturas"), isDirectory: true)
            .appendingPathComponent(año, isDirectory: true)
        try FileManager.default.createDirectory(at: carpeta, withIntermediateDirectories: true)
        let url = carpeta.appendingPathComponent(nombreArchivo(ingreso))
        try datos.write(to: url, options: .atomic)
        return url
    }

    /// Abre un correo nuevo en la app de correo con el PDF adjunto y el cliente como destinatario.
    @MainActor
    /// - `datos`: el PDF a adjuntar; por defecto, el documento adjunto de la factura.
    static func enviarPorCorreo(_ ingreso: Ingreso, datos: Data? = nil) {
        guard let datos = datos ?? ingreso.adjunto, !datos.isEmpty else { return }
        let carpeta = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString, isDirectory: true)
        let url = carpeta.appendingPathComponent(nombreArchivo(ingreso))
        do {
            try FileManager.default.createDirectory(at: carpeta, withIntermediateDirectories: true)
            try datos.write(to: url)
        } catch {
            NSSound.beep()
            return
        }
        let mes = ingreso.fecha.formatted(.dateTime.month(.wide).year())
        let asunto = String(localized: "Factura \(ingreso.numero) · \(mes)")
        let cuerpo = String(localized: """
        Hola:

        Te envío la factura \(ingreso.numero), por un total de \(ingreso.total.euros).

        Un saludo,
        \(Ajustes.texto(Ajustes.nombre))
        """)
        Correo.enviar(adjunto: url, para: ingreso.cliente?.email ?? "", asunto: asunto, cuerpo: cuerpo)
    }
}

// MARK: - Hoja de emisión

/// Emite un borrador como factura de Al Día: comprueba datos, genera el PDF, lo adjunta y lo archiva.
struct EmitirConAlDiaView: View {
    @Environment(\.modelContext) private var contexto
    @Environment(\.dismiss) private var dismiss
    @Query private var ingresos: [Ingreso]

    let ingreso: Ingreso
    @State private var numero: String
    @State private var fecha: Date
    @State private var emitida = false
    @State private var copia: URL?
    @State private var error: String?

    init(ingreso: Ingreso) {
        self.ingreso = ingreso
        _numero = State(initialValue: ingreso.numero)
        _fecha = State(initialValue: ingreso.fecha)
    }

    private var emisor: Emisor { Emisor() }

    private var numeroRepetido: Bool {
        let n = numero.trimmingCharacters(in: .whitespaces)
        return ingresos.contains { $0.id != ingreso.id && $0.numero == n }
    }

    var body: some View {
        VStack(spacing: 0) {
            Form {
                if emitida {
                    Section {
                        Label("Factura \(ingreso.numero) emitida y guardada.", systemImage: "checkmark.seal.fill")
                            .foregroundStyle(.green)
                            .font(.headline)
                        HStack {
                            Button("Abrir PDF") { abrir() }
                            Button("Imprimir…") { if let datos = ingreso.adjunto { FacturaPDF.imprimir(datos) } }
                            Button("Enviar por correo…") { FacturaPDF.enviarPorCorreo(ingreso) }
                                .keyboardShortcut(.defaultAction)
                            if let copia {
                                Button("Mostrar en Finder") { NSWorkspace.shared.activateFileViewerSelecting([copia]) }
                            }
                        }
                    } footer: {
                        if let copia {
                            Text(verbatim: copia.path(percentEncoded: false)).foregroundStyle(.secondary).textSelection(.enabled)
                        }
                    }
                } else {
                    Section {
                        LabeledContent("Cliente", value: ingreso.cliente?.nombre ?? "—")
                        LabeledContent("Total a cobrar") { Text(ingreso.total.euros).bold() }
                        TextField("Número", text: $numero)
                        DatePicker("Fecha de expedición", selection: $fecha, displayedComponents: .date)
                    } header: {
                        Text("Emitir con Al Día")
                    } footer: {
                        Text("Se genera el PDF con los datos obligatorios, la factura pasa a «Emitida» y deja de poder cambiarse sin una rectificativa.")
                            .foregroundStyle(.secondary)
                    }

                    let faltan = datosQueFaltan(ingreso, emisor: emisor)
                    if !faltan.isEmpty {
                        Section("Faltan datos obligatorios") {
                            ForEach(faltan, id: \.self) { Label($0, systemImage: "exclamationmark.circle").foregroundStyle(.red) }
                        }
                    }
                    if numeroRepetido {
                        Label("Ya hay otra factura con ese número.", systemImage: "exclamationmark.circle").foregroundStyle(.red)
                    }
                    if VeriFactu.obliga(en: fecha) {
                        Section {
                            Label("Desde el \(VeriFactu.obligatorioDesde.formatted(date: .long, time: .omitted)) las facturas hechas con programas deben cumplir VeriFactu. Emítela en la app de la AEAT (ficha «Emitir en la AEAT»). Hacienda ha anunciado un aplazamiento a octubre de 2028 pendiente de publicarse.",
                                  systemImage: "exclamationmark.triangle")
                                .foregroundStyle(.orange)
                        }
                    }
                    if let error {
                        Text(verbatim: error).foregroundStyle(.red)
                    }
                }
            }
            .formStyle(.grouped)

            HStack {
                Spacer()
                if emitida {
                    Button("Cerrar") { dismiss() }.keyboardShortcut(.cancelAction)
                } else {
                    Button("Cancelar") { dismiss() }.keyboardShortcut(.cancelAction)
                    Button("Emitir y crear PDF") { emitir() }
                        .keyboardShortcut(.defaultAction)
                        .disabled(!datosQueFaltan(ingreso, emisor: emisor).isEmpty || numeroRepetido
                                  || numero.trimmingCharacters(in: .whitespaces).isEmpty)
                }
            }
            .padding()
        }
        .frame(width: 540, height: 520)
    }

    private func emitir() {
        ingreso.numero = numero.trimmingCharacters(in: .whitespaces)
        ingreso.fecha = fecha
        let datos = FacturaPDF.generar(ingreso, emisor: emisor)
        ingreso.adjunto = datos
        ingreso.adjuntoNombre = FacturaPDF.nombreArchivo(ingreso)
        ingreso.estado = .emitida
        try? contexto.save()
        do {
            copia = try FacturaPDF.guardarCopia(datos, de: ingreso)
        } catch {
            self.error = String(localized: "La factura está emitida, pero no se pudo guardar la copia en Documentos: \(error.localizedDescription)")
        }
        emitida = true
    }

    private func abrir() {
        if let copia { NSWorkspace.shared.open(copia) }
        else if let datos = ingreso.adjunto, let nombre = ingreso.adjuntoNombre { Adjuntos.abrir(datos, nombre: nombre) }
    }
}


// MARK: - Ver la factura

/// Visor de PDF de PDFKit.
private struct VisorPDF: NSViewRepresentable {
    let datos: Data

    func makeNSView(context: Context) -> PDFView {
        let vista = PDFView()
        vista.autoScales = true
        vista.displayMode = .singlePageContinuous
        vista.backgroundColor = .windowBackgroundColor
        vista.document = PDFDocument(data: datos)
        return vista
    }

    func updateNSView(_ vista: PDFView, context: Context) {
        if vista.document?.dataRepresentation() != datos { vista.document = PDFDocument(data: datos) }
    }
}

/// La factura tal como la genera Al Día, con guardar, imprimir y enviar.
/// Los borradores llevan la marca de agua «BORRADOR» y no se pueden enviar.
struct VistaFacturaView: View {
    @Environment(\.dismiss) private var dismiss
    let ingreso: Ingreso
    @State private var datos = Data()

    var body: some View {
        VStack(spacing: 0) {
            if datos.isEmpty {
                ProgressView().frame(maxWidth: .infinity, maxHeight: .infinity)
            } else {
                VisorPDF(datos: datos)
            }
            Divider()
            HStack {
                if ingreso.esBorrador {
                    Label("Borrador: emítela para poder enviarla.", systemImage: "info.circle")
                        .foregroundStyle(.secondary)
                }
                Spacer()
                Button("Cerrar") { dismiss() }.keyboardShortcut(.cancelAction)
                Button("Guardar PDF…") { guardar() }
                Button("Imprimir…") { FacturaPDF.imprimir(datos) }
                    .keyboardShortcut("p")
                Button("Enviar por correo…") { FacturaPDF.enviarPorCorreo(ingreso, datos: datos) }
                    .keyboardShortcut(.defaultAction)
                    .disabled(ingreso.esBorrador)
            }
            .disabled(datos.isEmpty)
            .padding()
        }
        .frame(width: 600, height: 640)
        .onAppear { datos = FacturaPDF.generar(ingreso, borrador: ingreso.esBorrador) }
    }

    private func guardar() {
        let panel = NSSavePanel()
        panel.allowedContentTypes = [.pdf]
        panel.nameFieldStringValue = ingreso.esBorrador
            ? String(localized: "Borrador \(ingreso.numero).pdf")
            : FacturaPDF.nombreArchivo(ingreso)
        if panel.runModal() == .OK, let url = panel.url {
            do { try datos.write(to: url, options: .atomic) } catch { NSSound.beep() }
        }
    }
}
