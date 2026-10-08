import Foundation
import Testing
@testable import AlDiaCore

private func fecha(_ año: Int, _ mes: Int, _ dia: Int) -> Date {
    Calendar.fiscal.date(from: DateComponents(year: año, month: mes, day: dia))!
}

@Suite struct LectorTicketTests {
    @Test func facturaCompleta() {
        let texto = """
        TIENDA EJEMPLO SL
        CIF: B-87654321
        C/ Mayor 5, Madrid
        FACTURA Nº: A-2026/1234
        Fecha: 12/09/2026
        Cliente: Tu Nombre  NIF 00000000T
        Teclado 1 82,64
        Base imponible 82,64
        IVA 21% 17,35
        TOTAL 99,99 €
        """
        let g = LectorTicket.analizar(texto, miNIF: "00000000-T")
        #expect(g.proveedor == "TIENDA EJEMPLO SL")
        #expect(g.nifProveedor == "B87654321")
        #expect(g.numeroFactura == "A-2026/1234")
        #expect(g.fecha == fecha(2026, 9, 12))
        #expect(g.tipoIVA == 21)
        #expect(g.base == Decimal(string: "82.64"))
        #expect(g.cuotaIVA == Decimal(string: "17.35"))
        #expect(g.aMiNombre)
    }

    @Test func ticketSoloConTotal() {
        let texto = """
        SUPERMERCADO EJEMPLO
        NIF A12345674
        FACTURA SIMPLIFICADA 0042-000123
        12-03-26 18:45
        BANDAS ELASTICAS 12,10
        TOTAL 12,10
        IVA INCLUIDO 21%
        """
        let g = LectorTicket.analizar(texto, miNIF: "00000000T")
        #expect(g.proveedor == "SUPERMERCADO EJEMPLO")
        #expect(g.nifProveedor == "A12345674")
        #expect(g.numeroFactura == "0042-000123")
        #expect(g.fecha == fecha(2026, 3, 12))
        #expect(g.base == 10)
        #expect(g.cuotaIVA == Decimal(string: "2.1"))
        #expect(!g.aMiNombre)
    }

    /// Factura de un servicio en la nube: etiquetas e importes en columnas separadas,
    /// fecha con el mes en letra, el cliente arriba y el proveedor (irlandés) abajo.
    @Test func facturaEnColumnasDeProveedorUE() {
        let texto = """
        Factura
        Número de factura: 1234567890-1
        Facturar a
        Tu Nombre Apellido
        C/ Mayor, 1
        28001 Madrid
        Spain
        Número de IVA: ES 00000000T
        Detalles
        Fecha de la factura
        ..............................................................
        16 sept 2026
        Total en EUR
        Subtotal en EUR
        I.V.A. (21%)
        Total en EUR
        Capital social: 1.000.000 EUR
        Proveedor Nube Limited
        Dublin 4
        Ireland
        Número de IVA: IE1234567X
        21,99 €
        18,17 €
        3,82 €
        21,99 €
        """
        let g = LectorTicket.analizar(texto, miNIF: "00000000T", miNombre: "Tu Nombre Apellido")
        #expect(g.proveedor == "Proveedor Nube Limited")
        #expect(g.nifProveedor == "IE1234567X")
        #expect(g.proveedorExtranjero)
        #expect(g.numeroFactura == "1234567890-1")
        #expect(g.fecha == fecha(2026, 9, 16))
        #expect(g.base == Decimal(string: "18.17"))
        #expect(g.cuotaIVA == Decimal(string: "3.82"))
        #expect(g.tipoIVA == 21)
        #expect(g.aMiNombre)
    }

    @Test func fechasConMesEnLetra() {
        #expect(LectorTicket.analizar("Fecha: 3 de marzo de 2026").fecha == fecha(2026, 3, 3))
        #expect(LectorTicket.analizar("Invoice date Apr 16, 2026").fecha == fecha(2026, 4, 16))
        #expect(LectorTicket.analizar("16 abr. 2026").fecha == fecha(2026, 4, 16))
    }

    @Test func variantesDelNumero() {
        #expect(LectorTicket.analizar("FACTURA N: F-2026/0815").numeroFactura == "F-2026/0815")
        #expect(LectorTicket.analizar("Número de factura: 123").numeroFactura == "123")
        #expect(LectorTicket.analizar("Nº factura 2026-77").numeroFactura == "2026-77")
        #expect(LectorTicket.analizar("Factura de compra").numeroFactura == "")
    }

    @Test func textoVacio() {
        let g = LectorTicket.analizar("")
        #expect(g == GastoLeido())
    }
}
