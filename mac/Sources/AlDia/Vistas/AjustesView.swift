import AppKit
import SwiftData
import SwiftUI
import AlDiaCore

struct AjustesView: View {
    @AppStorage(Ajustes.nombre) private var nombre = ""
    @AppStorage(Ajustes.nif) private var nif = ""
    @AppStorage(Ajustes.direccion) private var direccion = ""
    @AppStorage(Ajustes.codigoPostal) private var codigoPostal = ""
    @AppStorage(Ajustes.poblacion) private var poblacion = ""
    @AppStorage(Ajustes.provincia) private var provincia = ""
    @AppStorage(Ajustes.email) private var email = ""
    @AppStorage(Ajustes.telefono) private var telefono = ""
    @AppStorage(Ajustes.iban) private var iban = ""
    @AppStorage(Ajustes.pieFactura) private var pie = ""
    @AppStorage(Ajustes.emailGestor) private var emailGestor = ""
    @AppStorage(Ajustes.appCorreo) private var appCorreo = "predeterminada"
    @AppStorage(Ajustes.ivaDefecto) private var iva = 21
    @AppStorage(Ajustes.retencionDefecto) private var retencion = 15
    @AppStorage(Ajustes.formatoNumeracion) private var formato = "anual"
    @AppStorage(Ajustes.prefijoNumeracion) private var prefijo = ""
    @Query(sort: \Ingreso.fecha) private var ingresos: [Ingreso]

    private var proximo: String {
        let hoy = Date.now
        return Ajustes.numeroSugerido(fecha: hoy, anteriores: ingresos.filter { $0.fecha <= hoy }.map(\.numero),
                                      existentes: ingresos.map(\.numero))
    }

    var body: some View {
        Form {
            Section("Tus datos") {
                TextField("Nombre", text: $nombre)
                TextField("NIF", text: $nif)
                TextField("Domicilio fiscal", text: $direccion, prompt: Text("C/ Mayor, 1, 2º A"))
                CamposLocalidad(codigoPostal: $codigoPostal, poblacion: $poblacion, provincia: $provincia)
                TextField("Email", text: $email)
                TextField("Teléfono", text: $telefono)
            }
            Section {
                CampoIBAN(iban: $iban)
                TextField("Texto al pie (opcional)", text: $pie, axis: .vertical)
                    .lineLimit(2...8)
                Button("Usar la cláusula de protección de datos recomendada") { pie = PieFactura.clausulaRGPD }
            } header: {
                Text("Facturas que emite Al Día")
            } footer: {
                Text("Aparecen en el PDF de las facturas. En el pie, {nombre}, {nif}, {domicilio} y {email} se sustituyen por tus datos.")
                    .foregroundStyle(.secondary)
            }
            Section {
                Picker("IVA", selection: $iva) {
                    ForEach([0, 4, 10, 21], id: \.self) { Text("\($0) %").tag($0) }
                }
                Picker("Retención IRPF", selection: $retencion) {
                    Text("15 % (general)").tag(15)
                    Text("7 % (primeros 3 años de actividad)").tag(7)
                    Text("Sin retención").tag(0)
                }
            } header: {
                Text("Valores por defecto en ingresos nuevos")
            }
            Section("Gestoría") {
                TextField("Email del gestor", text: $emailGestor, prompt: Text("gestoria@ejemplo.es"))
            }
            Section {
                Picker("Enviar correos con", selection: $appCorreo) {
                    Text("App de correo predeterminada").tag("predeterminada")
                    Text("Mail").tag("mail")
                }
            } header: {
                Text("Correo")
            } footer: {
                Text("Con Mail, el correo se prepara completo: destinatario, asunto, texto y adjunto. Necesita tu cuenta configurada en Mail; la primera vez macOS te pedirá permiso para que Al Día lo controle.")
                    .foregroundStyle(.secondary)
            }
            Section {
                Picker("Formato", selection: $formato) {
                    Text("Anual: 2027-001, 2027-002…").tag("anual")
                    Text("Continuar el de la última factura").tag("continuar")
                }
                if formato == "anual" {
                    TextField("Prefijo (opcional)", text: $prefijo, prompt: Text("F"))
                }
                LabeledContent("Próxima factura") { Text(verbatim: proximo.isEmpty ? "—" : proximo).monospaced() }
            } header: {
                Text("Numeración de facturas")
            } footer: {
                Text("La numeración anual vuelve a 001 cada 1 de enero. Usa el mismo formato en la serie de la app de la AEAT.")
                    .foregroundStyle(.secondary)
            }
            Section {
                LabeledContent("Base de datos") {
                    Button("Mostrar en Finder") {
                        NSWorkspace.shared.activateFileViewerSelecting([Almacen.carpeta])
                    }
                }
            } footer: {
                Text("Los datos y documentos adjuntos se guardan en ~/Library/Application Support/AlDia. Time Machine los incluye en sus copias.")
                    .foregroundStyle(.secondary)
            }
        }
        .formStyle(.grouped)
        .frame(width: 500, height: 660)
    }
}
