import CoreLocation
import SwiftUI
import AlDiaCore

/// Población a partir del código postal, con el geocodificador de Apple (solo se envía el código postal).
enum BuscadorPoblacion {
    static func poblacion(codigoPostal cp: String) async -> String? {
        let marcas = try? await CLGeocoder().geocodeAddressString(
            "\(cp), España", in: nil, preferredLocale: Locale(identifier: "es_ES"))
        let espanolas = (marcas ?? []).filter { $0.isoCountryCode == "ES" }
        let marca = espanolas.first { $0.postalCode == cp } ?? espanolas.first
        return marca?.locality ?? marca?.subAdministrativeArea
    }
}

/// Código postal, población y provincia: la provincia sale del prefijo y la población se busca sola
/// mientras el usuario no haya escrito otra.
struct CamposLocalidad: View {
    @Binding var codigoPostal: String
    @Binding var poblacion: String
    @Binding var provincia: String
    @State private var buscando = false
    @State private var rellenadaSola = ""

    var body: some View {
        TextField("Código postal", text: $codigoPostal, prompt: Text("28001"))
            .onChange(of: codigoPostal) { _, cp in completar(cp) }
        LabeledContent {
            HStack {
                TextField("Población", text: $poblacion).labelsHidden().multilineTextAlignment(.trailing)
                if buscando { ProgressView().controlSize(.small) }
            }
        } label: {
            Text("Población")
        }
        TextField("Provincia", text: $provincia)
    }

    private func completar(_ cp: String) {
        guard let prov = CodigoPostal.provincia(cp) else { return }
        provincia = prov
        guard poblacion.isEmpty || poblacion == rellenadaSola else { return }
        buscando = true
        Task {
            let encontrada = await BuscadorPoblacion.poblacion(codigoPostal: cp)
            buscando = false
            guard let encontrada, codigoPostal == cp, poblacion.isEmpty || poblacion == rellenadaSola else { return }
            poblacion = encontrada
            rellenadaSola = encontrada
        }
    }
}

/// IBAN agrupado de cuatro en cuatro mientras se escribe, con comprobación de los dígitos de control.
struct CampoIBAN: View {
    @Binding var iban: String

    var body: some View {
        LabeledContent("IBAN para cobros") {
            TextField("IBAN para cobros", text: $iban, prompt: Text("ES00 0000 0000 0000 0000 0000"))
                .labelsHidden()
                .multilineTextAlignment(.trailing)
                .monospaced()
        }
        // También al abrir: un IBAN guardado antes sin espacios se muestra ya agrupado
        .onAppear { agrupar(iban) }
        .onChange(of: iban) { _, nuevo in agrupar(nuevo) }
        let limpio = IBAN.normalizar(iban)
        if limpio.count >= 15 {
            if IBAN.esValido(iban) {
                Label("IBAN correcto", systemImage: "checkmark.circle.fill").foregroundStyle(.green)
            } else {
                Label("IBAN no válido: revisa las cifras", systemImage: "xmark.circle.fill").foregroundStyle(.red)
            }
        }
    }

    private func agrupar(_ texto: String) {
        let formateado = IBAN.formatear(texto)
        if formateado != texto { iban = formateado }
    }
}
