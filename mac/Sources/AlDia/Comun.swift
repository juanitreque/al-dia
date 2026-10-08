import AppKit
import SwiftUI
import UniformTypeIdentifiers
import AlDiaCore

// MARK: - Ajustes

enum Ajustes {
    static let nombre = "emisor.nombre"
    static let nif = "emisor.nif"
    // Datos que aparecen en las facturas que emite Al Día
    static let direccion = "emisor.direccion"
    static let codigoPostal = "emisor.cp"
    static let poblacion = "emisor.poblacion"
    static let provincia = "emisor.provincia"
    static let email = "emisor.email"
    static let telefono = "emisor.telefono"
    static let iban = "emisor.iban"
    static let pieFactura = "factura.pie"
    static let emailGestor = "gestor.email"
    /// "predeterminada" (la app de correo del sistema) o "mail" (Apple Mail por AppleScript).
    static let appCorreo = "correo.app"

    static func texto(_ clave: String) -> String {
        (UserDefaults.standard.string(forKey: clave) ?? "").trimmingCharacters(in: .whitespacesAndNewlines)
    }
    static let ivaDefecto = "defecto.iva"
    static let retencionDefecto = "defecto.retencion"
    /// "anual" (2027-001, por defecto) o "continuar" (sigue el formato de la última factura).
    static let formatoNumeracion = "numeracion.formato"
    static let prefijoNumeracion = "numeracion.prefijo"

    /// Número propuesto para una factura nueva según el formato elegido en Ajustes.
    static func numeroSugerido(fecha: Date, anteriores: [String], existentes: [String]) -> String {
        let d = UserDefaults.standard
        if d.string(forKey: formatoNumeracion) == "continuar" {
            return Numeracion.siguiente(despuesDe: anteriores.isEmpty ? existentes : anteriores,
                                        fecha: fecha, existentes: existentes)
        }
        return Numeracion.anual(prefijo: d.string(forKey: prefijoNumeracion) ?? "", fecha: fecha, existentes: existentes)
    }

    static var ivaPorDefecto: Decimal { Decimal(UserDefaults.standard.object(forKey: ivaDefecto) as? Int ?? 21) }
    static var retencionPorDefecto: Decimal { Decimal(UserDefaults.standard.object(forKey: retencionDefecto) as? Int ?? 15) }
}

enum Tipos {
    static let iva: [Decimal] = [0, 4, 10, 21]
    static let retencion: [Decimal] = [0, 7, 15]
    static let deducible: [Decimal] = [100, 50, 0]
}

// MARK: - Formato

extension Decimal {
    var euros: String {
        formatted(.currency(code: "EUR").locale(Locale(identifier: "es_ES")))
    }

    var porcentaje: String {
        "\(formatted(.number.locale(Locale(identifier: "es_ES")))) %"
    }
}

extension Date {
    var corta: String { formatted(.dateTime.day(.twoDigits).month(.twoDigits).year()) }
}

extension Trimestre {
    /// "3T 2026" en castellano, "Q3 2026" en inglés (solo para mostrar; `periodo` sigue siendo el dato).
    var nombre: String { String(localized: "\(numero)T \(String(ejercicio))") }
}

extension Periodo {
    var nombre: String {
        trimestre.map { String(localized: "\($0)T \(String(ejercicio))") } ?? String(ejercicio)
    }
}

/// Periodo guardado en un modelo presentado ("1T"…"4T", "0A") tal como se muestra.
func nombrePeriodo(_ periodo: String) -> String {
    if periodo == "0A" { return String(localized: "Anual") }
    if let n = Int(periodo.dropLast()), periodo.hasSuffix("T") { return String(localized: "\(n)T") }
    return periodo
}

/// Traduce un texto que llega como dato (p. ej. los avisos del lector de PDF, en castellano).
func traducido(_ texto: String) -> String {
    Bundle.main.localizedString(forKey: texto, value: texto, table: nil)
}

func añosDisponibles(_ fechas: [Date]) -> [Int] {
    let actual = Calendar.fiscal.component(.year, from: .now)
    let años = Set(fechas.map { Calendar.fiscal.component(.year, from: $0) }).union([actual])
    return años.sorted(by: >)
}

// MARK: - Controles

/// Campo de importe que acepta coma o punto decimal y se actualiza al escribir
/// (TextField(value:) en macOS solo confirma al pulsar Intro o cambiar de foco).
struct CampoImporte: View {
    let titulo: LocalizedStringKey
    @Binding var valor: Decimal
    var decimales: ClosedRange<Int> = 2...2
    @State private var texto = ""

    var body: some View {
        TextField(titulo, text: $texto, prompt: Text(decimales.lowerBound == 0 ? "0" : "0,00"))
            .multilineTextAlignment(.trailing)
            .monospacedDigit()
            .onAppear { texto = Importe.texto(valor, decimales: decimales) }
            .onChange(of: texto) { _, nuevo in
                let leido = Importe.parse(nuevo) ?? 0
                if leido != valor { valor = leido }
            }
            .onChange(of: valor) { _, nuevo in
                if (Importe.parse(texto) ?? 0) != nuevo { texto = Importe.texto(nuevo, decimales: decimales) }
            }
    }
}

struct SelectorTipo: View {
    let titulo: LocalizedStringKey
    let opciones: [Decimal]
    @Binding var valor: Decimal

    var body: some View {
        Picker(titulo, selection: $valor) {
            ForEach(opciones, id: \.self) { Text($0.porcentaje).tag($0) }
            if !opciones.contains(valor) { Text(valor.porcentaje).tag(valor) }
        }
    }
}

struct CampoAdjunto: View {
    @Binding var datos: Data?
    @Binding var nombre: String?
    @State private var importando = false

    var body: some View {
        LabeledContent("Documento") {
            HStack {
                if let datos, let nombre {
                    Button { Adjuntos.abrir(datos, nombre: nombre) } label: {
                        Label(nombre, systemImage: "doc.richtext")
                    }
                    .buttonStyle(.link)
                    .lineLimit(1)
                    Button("Quitar") { self.datos = nil; self.nombre = nil }
                } else {
                    Text("Arrastra aquí un PDF o imagen").foregroundStyle(.secondary)
                }
                Button("Adjuntar…") { importando = true }
            }
        }
        .fileImporter(isPresented: $importando, allowedContentTypes: [.pdf, .image]) { resultado in
            if case .success(let url) = resultado { cargar(url) }
        }
        .dropDestination(for: URL.self) { urls, _ in
            guard let url = urls.first else { return false }
            cargar(url)
            return true
        }
    }

    private func cargar(_ url: URL) {
        let acceso = url.startAccessingSecurityScopedResource()
        defer { if acceso { url.stopAccessingSecurityScopedResource() } }
        guard let contenido = try? Data(contentsOf: url) else { return }
        datos = contenido
        nombre = url.lastPathComponent
    }
}

enum Adjuntos {
    /// Abre el documento con la app predeterminada (Vista Previa) desde una copia temporal.
    static func abrir(_ datos: Data, nombre: String) {
        let carpeta = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString, isDirectory: true)
        let url = carpeta.appendingPathComponent(nombre)
        do {
            try FileManager.default.createDirectory(at: carpeta, withIntermediateDirectories: true)
            try datos.write(to: url)
            NSWorkspace.shared.open(url)
        } catch {
            NSSound.beep()
        }
    }
}

struct SelectorPeriodo: View {
    @Binding var periodo: Periodo
    let años: [Int]

    var body: some View {
        Picker("Ejercicio", selection: $periodo.ejercicio) {
            ForEach(años, id: \.self) { Text(String($0)).tag($0) }
        }
        Picker("Trimestre", selection: $periodo.trimestre) {
            Text("Año completo").tag(Int?.none)
            ForEach(1...4, id: \.self) { Text("\($0)T").tag(Int?.some($0)) }
        }
        .pickerStyle(.segmented)
        .fixedSize()
    }
}

/// Barra inferior de totales bajo una tabla.
struct BarraTotales: View {
    let elementos: [(LocalizedStringKey, String)]

    var body: some View {
        HStack(spacing: 20) {
            ForEach(elementos.indices, id: \.self) { i in
                HStack(spacing: 6) {
                    Text(elementos[i].0).foregroundStyle(.secondary)
                    Text(verbatim: elementos[i].1).monospacedDigit().fontWeight(.medium)
                }
            }
            Spacer()
        }
        .font(.callout)
        .padding(.horizontal, 16)
        .padding(.vertical, 10)
        .background(.bar)
    }
}

struct CeldaImporte: View {
    let valor: Decimal
    var body: some View {
        Text(valor.euros).monospacedDigit().frame(maxWidth: .infinity, alignment: .trailing)
    }
}

/// Botonera Cancelar / Guardar de las hojas de edición.
struct BotoneraEditor: View {
    let puedeGuardar: Bool
    let guardar: () -> Void
    @Environment(\.dismiss) private var dismiss

    var body: some View {
        HStack {
            Spacer()
            Button("Cancelar") { dismiss() }.keyboardShortcut(.cancelAction)
            Button("Guardar") { guardar(); dismiss() }
                .keyboardShortcut(.defaultAction)
                .disabled(!puedeGuardar)
        }
        .padding()
    }
}

// MARK: - CSV

struct DocumentoCSV: FileDocument {
    static var readableContentTypes: [UTType] { [.commaSeparatedText] }
    var texto: String

    init(filas: [[String]]) {
        texto = filas.map { $0.map(Self.escapar).joined(separator: ";") }.joined(separator: "\r\n")
    }

    init(configuration: ReadConfiguration) throws {
        texto = String(decoding: configuration.file.regularFileContents ?? Data(), as: UTF8.self)
    }

    func fileWrapper(configuration: WriteConfiguration) throws -> FileWrapper {
        // BOM para que Excel detecte UTF-8; separador ";" y coma decimal para Excel en español.
        FileWrapper(regularFileWithContents: Data(("\u{FEFF}" + texto).utf8))
    }

    static func escapar(_ campo: String) -> String {
        guard campo.contains(where: { ";\"\n\r".contains($0) }) else { return campo }
        return "\"" + campo.replacingOccurrences(of: "\"", with: "\"\"") + "\""
    }

    static func importe(_ valor: Decimal) -> String {
        valor.formatted(.number.precision(.fractionLength(2)).grouping(.never).locale(Locale(identifier: "es_ES")))
    }
}

extension String {
    var capitalizedFirst: String { prefix(1).uppercased() + dropFirst() }
}

/// Fichero de texto plano UTF-8 (formatos de importación de la app de la AEAT).
struct DocumentoTexto: FileDocument {
    static var readableContentTypes: [UTType] { [.plainText] }
    var texto: String

    init(texto: String) { self.texto = texto }

    init(configuration: ReadConfiguration) throws {
        texto = String(decoding: configuration.file.regularFileContents ?? Data(), as: UTF8.self)
    }

    func fileWrapper(configuration: WriteConfiguration) throws -> FileWrapper {
        FileWrapper(regularFileWithContents: Data(texto.utf8))
    }
}

enum AEAT {
    /// Trámite "Aplicación gratuita VERI*FACTU" en la sede electrónica.
    static let aplicacion = URL(string: "https://sede.agenciatributaria.gob.es/Sede/procedimientoini/IZ86.shtml")!
}

enum Portapapeles {
    static func copiar(_ texto: String) {
        NSPasteboard.general.clearContents()
        NSPasteboard.general.setString(texto, forType: .string)
    }
}

/// Dato con botón de copiar, para pasarlo a otra aplicación sin errores.
struct DatoCopiable: View {
    let titulo: LocalizedStringKey
    let valor: String
    @State private var copiado = false

    var body: some View {
        LabeledContent(titulo) {
            HStack(spacing: 8) {
                Text(verbatim: valor.isEmpty ? "—" : valor).textSelection(.enabled).monospacedDigit()
                Button {
                    Portapapeles.copiar(valor)
                    copiado = true
                    Task { try? await Task.sleep(for: .seconds(1.2)); copiado = false }
                } label: {
                    Image(systemName: copiado ? "checkmark" : "doc.on.doc")
                        .frame(width: 16)
                }
                .buttonStyle(.borderless)
                .help("Copiar")
                .disabled(valor.isEmpty)
            }
        }
    }
}

extension Decimal {
    /// "19,00" sin símbolo de moneda, como se teclea en formularios.
    var importeSimple: String {
        formatted(.number.precision(.fractionLength(2)).grouping(.never).locale(Locale(identifier: "es_ES")))
    }

    var cantidad: String {
        formatted(.number.precision(.fractionLength(0...2)).locale(Locale(identifier: "es_ES")))
    }
}
