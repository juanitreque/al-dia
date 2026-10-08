import Testing
@testable import AlDiaCore

@Suite struct CodigoPostalTests {
    @Test func provinciaPorPrefijo() {
        #expect(CodigoPostal.provincia("28001") == "Madrid")
        #expect(CodigoPostal.provincia("29600") == "Málaga")
        #expect(CodigoPostal.provincia("48001") == "Bizkaia")
        #expect(CodigoPostal.provincia("52001") == "Melilla")
    }

    @Test func codigosNoValidos() {
        #expect(CodigoPostal.provincia("53000") == nil)
        #expect(CodigoPostal.provincia("00100") == nil)
        #expect(CodigoPostal.provincia("2800") == nil)
        #expect(CodigoPostal.provincia("28O01") == nil)
    }
}

@Suite struct IBANTests {
    // IBAN de ejemplo publicado en la documentación del estándar (no es una cuenta real)
    private let ejemplo = "ES9121000418450200051332"

    @Test func formatea() {
        #expect(IBAN.formatear("es91 2100-0418 450200051332") == "ES91 2100 0418 4502 0005 1332")
        #expect(IBAN.formatear("ES912") == "ES91 2")
        #expect(IBAN.formatear("") == "")
    }

    @Test func validaDigitosDeControl() {
        #expect(IBAN.esValido(ejemplo))
        #expect(IBAN.esValido("ES91 2100 0418 4502 0005 1332"))
        #expect(IBAN.esValido("GB82WEST12345698765432"))
        #expect(!IBAN.esValido("ES9121000418450200051333"))  // un dígito cambiado
        #expect(!IBAN.esValido("ES912100041845020005133"))   // longitud incorrecta para España
        #expect(!IBAN.esValido("ES00 0000 0000 0000 0000 0000"))
    }
}

@Suite struct PieFacturaTests {
    @Test func rellenaMarcadores() {
        let texto = PieFactura.rellenar("{nombre} ({nif}) · {domicilio} · {email}",
                                        nombre: "Tu Nombre", nif: "00000000T", domicilio: "C/ Mayor, 1, 28001 Madrid", email: "tu@correo.es")
        #expect(texto == "Tu Nombre (00000000T) · C/ Mayor, 1, 28001 Madrid · tu@correo.es")
        #expect(PieFactura.clausulaRGPD.contains("{email}"))
    }
}
