import AppKit
import PDFKit
import Vision

/// Obtiene el texto de un ticket o factura: la capa de texto del PDF si la tiene,
/// o reconocimiento óptico (Vision, en el propio Mac) para fotos y PDF escaneados.
enum LecturaDocumentos {
    static func texto(de datos: Data, esPDF: Bool) async -> String {
        if esPDF, let documento = PDFDocument(data: datos) {
            let texto = documento.string ?? ""
            if texto.trimmingCharacters(in: .whitespacesAndNewlines).count > 20 { return texto }
            var paginas: [String] = []
            for i in 0..<min(documento.pageCount, 3) {
                if let pagina = documento.page(at: i), let imagen = imagen(de: pagina) {
                    paginas.append(await reconocer(imagen))
                }
            }
            return paginas.joined(separator: "\n")
        }
        guard let imagen = NSImage(data: datos)?.cgImage(forProposedRect: nil, context: nil, hints: nil) else { return "" }
        return await reconocer(imagen)
    }

    private static func imagen(de pagina: PDFPage) -> CGImage? {
        let caja = pagina.bounds(for: .mediaBox)
        let miniatura = pagina.thumbnail(of: CGSize(width: caja.width * 2.5, height: caja.height * 2.5), for: .mediaBox)
        return miniatura.cgImage(forProposedRect: nil, context: nil, hints: nil)
    }

    private static func reconocer(_ imagen: CGImage) async -> String {
        await Task.detached(priority: .userInitiated) {
            let peticion = VNRecognizeTextRequest()
            peticion.recognitionLevel = .accurate
            peticion.recognitionLanguages = ["es-ES", "en-US"]
            peticion.usesLanguageCorrection = true
            try? VNImageRequestHandler(cgImage: imagen).perform([peticion])
            return filas(peticion.results ?? [])
        }.value
    }

    /// Vision devuelve trozos sueltos; en un ticket la etiqueta ("TOTAL") y el importe quedan
    /// separados en la misma fila. Se agrupan por altura y se ordenan de izquierda a derecha.
    private static func filas(_ observaciones: [VNRecognizedTextObservation]) -> String {
        let trozos = observaciones
            .compactMap { o in o.topCandidates(1).first.map { (caja: o.boundingBox, texto: $0.string) } }
            .sorted { $0.caja.midY > $1.caja.midY }
        var filas: [[(caja: CGRect, texto: String)]] = []
        for trozo in trozos {
            if let referencia = filas.last?.first,
               abs(referencia.caja.midY - trozo.caja.midY) < max(referencia.caja.height, trozo.caja.height) * 0.5 {
                filas[filas.count - 1].append(trozo)
            } else {
                filas.append([trozo])
            }
        }
        return filas
            .map { $0.sorted { $0.caja.minX < $1.caja.minX }.map(\.texto).joined(separator: " ") }
            .joined(separator: "\n")
    }
}
