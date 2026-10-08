import Foundation
import Testing
@testable import AlDiaCore

private func fecha(_ año: Int, _ mes: Int, _ dia: Int) -> Date {
    Calendar.fiscal.date(from: DateComponents(year: año, month: mes, day: dia))!
}

/// Texto con la misma estructura que extrae PDFKit de las facturas reales (datos ficticios).
private let muestra = """
DATOS CLIENTE:
Empresa Ejemplo, SL
C/ Mayor, 1
FACTURA 28001 Madrid
Madrid
Número: F0126 Fecha: 31/01/2026 B12345678
CONCEPTO UNIDAD PRECIO HONORARIOS
CONSULTORIA 20 19,00 380,00
PERSONAL(2) 3 35,00 105,00
Base I.V.A. % Base I.R.P.F. %
485,00 21 485,00 15
Honoraios I.V.A. I.R.P.F.
IMPORTE LÍQUIDO
485,00 101,85 -72,75 514,10
Texto legal con números como 28001 y 21. que no deben confundir al lector.
"""

@Suite struct LectorFacturaTests {
    @Test func leeLaPlantilla() throws {
        let f = try #require(LectorFactura.analizar(muestra))
        #expect(f.numero == "F0126")
        #expect(f.fecha == fecha(2026, 1, 31))
        #expect(f.clienteNombre == "Empresa Ejemplo, SL")
        #expect(f.clienteNIF == "B12345678")
        #expect(f.lineas == [
            LineaLeida(concepto: "CONSULTORIA", cantidad: 20, precio: 19, importe: 380),
            LineaLeida(concepto: "PERSONAL(2)", cantidad: 3, precio: 35, importe: 105),
        ])
        #expect(f.base == 485)
        #expect(f.tipoIVA == 21)
        #expect(f.cuotaIVA == Decimal(string: "101.85"))
        #expect(f.tipoRetencion == 15)
        #expect(f.retencion == Decimal(string: "72.75"))
        #expect(f.total == Decimal(string: "514.10"))
        #expect(f.avisos.isEmpty)
    }

    @Test func avisaSiNoCuadra() throws {
        let alterada = muestra.replacingOccurrences(of: "PERSONAL(2) 3 35,00 105,00\n", with: "")
        let f = try #require(LectorFactura.analizar(alterada))
        #expect(f.avisos == ["Las líneas no suman la base"])
    }

    @Test func ignoraTextoSinFactura() {
        #expect(LectorFactura.analizar("Un PDF cualquiera") == nil)
    }
}

@Suite struct NumeracionTests {
    @Test func patronMesAño() {
        #expect(Numeracion.siguiente(despuesDe: ["F0826", "F0926"], fecha: fecha(2026, 10, 31)) == "F1026")
        #expect(Numeracion.siguiente(despuesDe: ["F0926", "F1026"], fecha: fecha(2026, 10, 31)) == "F1026-2")
        #expect(Numeracion.siguiente(despuesDe: ["F1226"], fecha: fecha(2027, 1, 31)) == "F0127")
        // Un borrador posterior ya ocupa M1026
        #expect(Numeracion.siguiente(despuesDe: ["F0926"], fecha: fecha(2026, 10, 3), existentes: ["F1026"]) == "F1026-2")
        #expect(Numeracion.siguiente(despuesDe: ["A001"], fecha: .now, existentes: ["A002"]) == "A003")
    }

    @Test func serieAnual() {
        #expect(Numeracion.anual(fecha: fecha(2027, 1, 15), existentes: ["F1226", "2026-003"]) == "2027-001")
        #expect(Numeracion.anual(fecha: fecha(2027, 3, 1), existentes: ["2027-001", "2027-002", "2027-010"]) == "2027-011")
        #expect(Numeracion.anual(prefijo: "F", fecha: fecha(2027, 3, 1), existentes: ["2027-005", "F2027-002"]) == "F2027-003")
        #expect(Numeracion.anual(fecha: fecha(2027, 3, 1), existentes: ["2027-999"]) == "2027-1000")
    }

    @Test func cambioDeAño() {
        #expect(Numeracion.siguiente(despuesDe: ["2026-087"], fecha: fecha(2027, 1, 10)) == "2027-001")
        #expect(Numeracion.siguiente(despuesDe: ["F2026/12"], fecha: fecha(2027, 1, 10)) == "F2027/01")
        #expect(Numeracion.siguiente(despuesDe: ["2026-087"], fecha: fecha(2026, 12, 10)) == "2026-088")
    }

    @Test func secuencial() {
        #expect(Numeracion.siguiente(despuesDe: ["2026-007"], fecha: .now) == "2026-008")
        #expect(Numeracion.siguiente(despuesDe: ["A099"], fecha: .now) == "A100")
        #expect(Numeracion.siguiente(despuesDe: [], fecha: .now) == "")
    }
}

@Suite struct ExportacionAEATTests {
    @Test func clientes() {
        let texto = ExportacionAEAT.clientes([
            .init(nombre: "Empresa #1, SL", nif: "B12345678", codigoPostal: "28001", poblacion: "Madrid"),
        ])
        #expect(texto == ExportacionAEAT.cabeceraClientes + "\r\nEmpresa  1, SL#B12345678##28001#Madrid##ES###\r\n")
    }

    @Test func productos() {
        let texto = ExportacionAEAT.productos([
            .init(id: "WEB F", descripcion: "Diseño web", precio: 25, tipoIVA: 21),
        ])
        #expect(texto.hasSuffix("\r\nWEB_F#Diseño web#25.00###01#01#S1#21.00#\r\n"))
    }
}
