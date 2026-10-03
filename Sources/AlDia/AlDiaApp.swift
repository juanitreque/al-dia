import AppKit
import SwiftData
import SwiftUI

final class Delegado: NSObject, NSApplicationDelegate {
    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { true }
}

@main
struct AlDiaApp: App {
    @NSApplicationDelegateAdaptor(Delegado.self) private var delegado
    private let contenedor: ModelContainer

    init() {
        do {
            contenedor = try Almacen.contenedor()
            if Demo.activo { MainActor.assumeIsolated { Demo.rellenar(contenedor) } }
        } catch {
            fatalError("No se pudo abrir la base de datos en \(Almacen.carpeta.path): \(error)")
        }
    }

    var body: some Scene {
        Window("Al Día", id: "principal") {
            VentanaPrincipal()
        }
        .modelContainer(contenedor)
        .defaultSize(width: 1150, height: 720)
        .commands {
            CommandGroup(replacing: .newItem) {}
        }

        Settings {
            AjustesView()
        }
        .modelContainer(contenedor)
    }
}

enum Seccion: String, CaseIterable, Identifiable {
    case panel = "Panel"
    case ingresos = "Ingresos"
    case gastos = "Gastos"
    case modelos = "Modelos fiscales"
    case clientes = "Clientes"
    case servicios = "Servicios"

    var id: String { rawValue }

    var nombre: LocalizedStringKey {
        switch self {
        case .panel: "Panel"
        case .ingresos: "Ingresos"
        case .gastos: "Gastos"
        case .modelos: "Modelos fiscales"
        case .clientes: "Clientes"
        case .servicios: "Servicios"
        }
    }

    var icono: String {
        switch self {
        case .panel: "gauge.with.dots.needle.33percent"
        case .ingresos: "arrow.down.left.circle"
        case .gastos: "arrow.up.right.circle"
        case .modelos: "building.columns"
        case .clientes: "person.2"
        case .servicios: "tag"
        }
    }
}

struct VentanaPrincipal: View {
    @State private var seccion: Seccion? = .panel

    var body: some View {
        NavigationSplitView {
            List(Seccion.allCases, selection: $seccion) { s in
                Label(s.nombre, systemImage: s.icono).tag(s)
            }
            .navigationSplitViewColumnWidth(min: 170, ideal: 190)
        } detail: {
            switch seccion ?? .panel {
            case .panel: PanelView(irA: { seccion = $0 })
            case .ingresos: IngresosView()
            case .gastos: GastosView()
            case .modelos: ModelosView()
            case .clientes: ClientesView()
            case .servicios: ServiciosView()
            }
        }
    }
}
