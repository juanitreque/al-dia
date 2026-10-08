import Foundation
import Testing
@testable import AlDiaCore

private func fecha(_ año: Int, _ mes: Int, _ dia: Int) -> Date {
    Calendar.fiscal.date(from: DateComponents(year: año, month: mes, day: dia))!
}

private func ingreso(_ f: Date, base: Decimal, iva: Decimal = 21, retencion: Decimal = 15) -> DatosIngreso {
    DatosIngreso(fecha: f, base: base, tipoIVA: iva,
                 cuotaIVA: Calculo.porcentaje(iva, de: base),
                 retencion: Calculo.porcentaje(retencion, de: base))
}

@Suite struct ImporteTests {
    @Test func parseaFormatosEspañoles() {
        #expect(Importe.parse("45,50") == Decimal(string: "45.5"))
        #expect(Importe.parse("1.234,56 €") == Decimal(string: "1234.56"))
        #expect(Importe.parse("45.5") == Decimal(string: "45.5"))
        #expect(Importe.parse("-12,3") == Decimal(string: "-12.3"))
        #expect(Importe.parse("") == nil)
        #expect(Importe.parse("12abc") == nil)
    }

    @Test func formateaParaEditar() {
        #expect(Importe.texto(Decimal(string: "1234.5")!) == "1234,50")
        #expect(Importe.texto(0) == "")
    }

    @Test func redondeaHalfUp() {
        #expect(Calculo.porcentaje(21, de: Decimal(string: "12.5")!) == Decimal(string: "2.63"))
    }
}

@Suite struct TrimestreTests {
    @Test func detectaTrimestre() {
        #expect(Trimestre.de(fecha(2026, 10, 3)) == Trimestre(ejercicio: 2026, numero: 4))
        #expect(Trimestre.de(fecha(2026, 3, 31)) == Trimestre(ejercicio: 2026, numero: 1))
        #expect(Trimestre.aPresentar(hoy: fecha(2026, 1, 15)) == Trimestre(ejercicio: 2025, numero: 4))
    }

    @Test func plazos() {
        #expect(Trimestre(ejercicio: 2026, numero: 3).plazoPresentacion == fecha(2026, 10, 20))
        // 30-ene-2027 es sábado → lunes 1 de febrero
        #expect(Trimestre(ejercicio: 2026, numero: 4).plazoPresentacion == fecha(2027, 2, 1))
    }

    @Test func periodoAnual() {
        let p = Periodo(ejercicio: 2026)
        #expect(p.contiene(fecha(2026, 12, 31)))
        #expect(!p.contiene(fecha(2027, 1, 1)))
    }
}

@Suite struct Modelo303Tests {
    @Test func liquidaTrimestre() {
        let t = Trimestre(ejercicio: 2026, numero: 3)
        let ingresos = [
            ingreso(fecha(2026, 7, 31), base: 600),
            ingreso(fecha(2026, 8, 31), base: 400),
            ingreso(fecha(2026, 10, 1), base: 999), // fuera del trimestre
        ]
        let gastos = [
            DatosGasto(fecha: fecha(2026, 7, 10), base: 100, cuotaIVA: 21),
            DatosGasto(fecha: fecha(2026, 8, 10), base: 200, cuotaIVA: 42, porcentajeDeducibleIVA: 50), // vehículo
            DatosGasto(fecha: fecha(2026, 9, 1), base: 50, cuotaIVA: Decimal(string: "10.5")!, facturaCompleta: false), // ticket
            DatosGasto(fecha: fecha(2026, 9, 5), base: 300, cuotaIVA: 0), // RETA, sin IVA
        ]
        let l = Liquidacion303(trimestre: t, ingresos: ingresos, gastos: gastos)
        #expect(l.devengos.count == 1)
        #expect(l.devengos[0].base == 1000)
        #expect(l.totalDevengado == 210)
        #expect(l.baseCorrientes == 200)
        #expect(l.cuotaCorrientes == 42)
        #expect(l.resultado == 168)
        #expect(l.resultado(compensando: 200) == -32)
    }
}

@Suite struct Modelo130Tests {
    @Test func acumulaDesdeEnero() {
        let t = Trimestre(ejercicio: 2026, numero: 2)
        let ingresos = [
            ingreso(fecha(2026, 2, 1), base: 1000),
            ingreso(fecha(2026, 5, 1), base: 2000),
            ingreso(fecha(2025, 12, 1), base: 5000), // otro ejercicio
        ]
        let gastos = [
            DatosGasto(fecha: fecha(2026, 3, 1), base: 400, cuotaIVA: 84),
            DatosGasto(fecha: fecha(2026, 4, 1), base: 100, cuotaIVA: 21, facturaCompleta: false), // IVA no deducible → gasto
            DatosGasto(fecha: fecha(2026, 4, 2), base: 4000, cuotaIVA: 840, bienInversion: true), // se amortiza
        ]
        let l = Liquidacion130(trimestre: t, ingresos: ingresos, gastos: gastos, pagosAnteriores: 0)
        #expect(l.ingresos == 3000)
        #expect(l.gastos == 521)
        #expect(l.rendimiento == 2479)
        #expect(l.cuota == Decimal(string: "495.8"))
        #expect(l.retenciones == 450)
        #expect(l.resultado == Decimal(string: "45.8"))
    }

    @Test func reglaDelSetentaPorCiento() {
        let ingresos = [
            ingreso(fecha(2026, 1, 1), base: 800),
            ingreso(fecha(2026, 2, 1), base: 200, retencion: 0),
        ]
        #expect(Liquidacion130.porcentajeConRetencion(ingresos, ejercicio: 2026) == 80)
        #expect(Liquidacion130.porcentajeConRetencion(ingresos, ejercicio: 2025) == nil)
    }
}
