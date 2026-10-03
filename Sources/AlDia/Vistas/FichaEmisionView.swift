import AppKit
import SwiftData
import SwiftUI
import AlDiaCore

/// Guía para pasar un borrador a la app gratuita VERI*FACTU de la AEAT y marcarlo como emitido.
struct FichaEmisionView: View {
    @Environment(\.modelContext) private var contexto
    @Environment(\.dismiss) private var dismiss
    let ingreso: Ingreso

    @State private var numeroOficial: String
    @State private var fechaOficial: Date
    @State private var adjunto: Data?
    @State private var adjuntoNombre: String?

    init(ingreso: Ingreso) {
        self.ingreso = ingreso
        _numeroOficial = State(initialValue: ingreso.numero)
        _fechaOficial = State(initialValue: ingreso.fecha)
        _adjunto = State(initialValue: ingreso.adjunto)
        _adjuntoNombre = State(initialValue: ingreso.adjuntoNombre)
    }

    var body: some View {
        VStack(spacing: 0) {
            Form {
                Section {
                    HStack {
                        VStack(alignment: .leading, spacing: 4) {
                            Text("Emitir \(ingreso.numero) en la AEAT").font(.title3.bold())
                            Text("Aplicación gratuita VERI*FACTU → Emisión de facturas. Copia cada dato con su botón.")
                                .foregroundStyle(.secondary)
                        }
                        Spacer()
                        Button("Abrir la app de la AEAT") { NSWorkspace.shared.open(AEAT.aplicacion) }
                    }
                }

                Section("1 · Destinatario (Seleccionar destinatario)") {
                    DatoCopiable(titulo: "Nombre o razón social", valor: ingreso.cliente?.nombre ?? "")
                    DatoCopiable(titulo: "NIF", valor: ingreso.cliente?.nif ?? "")
                }

                Section("2 · Datos de la factura") {
                    DatoCopiable(titulo: "Fecha de expedición", valor: ingreso.fecha.corta)
                    DatoCopiable(titulo: "Descripción", valor: ingreso.concepto)
                    LabeledContent("Serie y número") {
                        Text("Elige tu serie: la AEAT pone el número").foregroundStyle(.secondary)
                    }
                }

                Section("3 · Productos (Recuperar producto/servicio)") {
                    if ingreso.lineasOrdenadas.isEmpty {
                        DatoCopiable(titulo: "Importe", valor: ingreso.base.importeSimple)
                    }
                    ForEach(ingreso.lineasOrdenadas) { l in
                        VStack(alignment: .leading, spacing: 6) {
                            Text(l.codigo.isEmpty ? l.concepto : "\(l.codigo) · \(l.concepto)").fontWeight(.medium)
                            DatoCopiable(titulo: "Cantidad", valor: l.cantidad.cantidad)
                            DatoCopiable(titulo: "Precio unitario", valor: l.precio.importeSimple)
                        }
                    }
                    LabeledContent("IVA") { Text(ingreso.tipoIVA.porcentaje) }
                }

                Section("4 · Ajustes de la factura") {
                    DatoCopiable(titulo: "Retención global (%)", valor: ingreso.tipoRetencion.cantidad)
                    DatoCopiable(titulo: "Importe retenido", valor: ingreso.retencion.importeSimple)
                }

                Section {
                    LabeledContent("Base imponible", value: ingreso.base.euros)
                    LabeledContent("IVA", value: ingreso.cuotaIVA.euros)
                    LabeledContent("Retención", value: (-ingreso.retencion).euros)
                    LabeledContent("Total") { Text(ingreso.total.euros).bold() }
                } header: {
                    Text("5 · Comprueba antes de firmar")
                } footer: {
                    Text("Validar/Generar factura → Firmar y enviar → Conforme. Después pulsa Descargar → PDF.")
                        .foregroundStyle(.secondary)
                }

                Section("6 · Ya emitida: completa con los datos oficiales") {
                    TextField("Número oficial", text: $numeroOficial)
                    DatePicker("Fecha de expedición", selection: $fechaOficial, displayedComponents: .date)
                    CampoAdjunto(datos: $adjunto, nombre: $adjuntoNombre)
                    if adjunto == nil {
                        Label("Adjunta el PDF que descargas de la AEAT (lleva el código QR).", systemImage: "info.circle")
                            .foregroundStyle(.secondary)
                    }
                }
            }
            .formStyle(.grouped)

            HStack {
                Spacer()
                Button("Cerrar") { dismiss() }.keyboardShortcut(.cancelAction)
                Button("Marcar como emitida") { marcarEmitida() }
                    .keyboardShortcut(.defaultAction)
                    .disabled(numeroOficial.trimmingCharacters(in: .whitespaces).isEmpty)
            }
            .padding()
        }
        .frame(width: 600, height: 680)
    }

    private func marcarEmitida() {
        ingreso.numero = numeroOficial.trimmingCharacters(in: .whitespaces)
        ingreso.fecha = fechaOficial
        ingreso.adjunto = adjunto
        ingreso.adjuntoNombre = adjuntoNombre
        ingreso.estado = .emitida
        try? contexto.save()
        dismiss()
    }
}
