# Al Día

**Contabilidad sencilla para autónomos en España: apps nativas para Mac y Windows.**
Mantente al día con Hacienda: prepara tus facturas, apunta los gastos deducibles y ten calculados tus modelos trimestrales (303 / 130), casilla por casilla.

[Read in English](README.md) · macOS 15+ (Swift/SwiftUI) · Windows 10/11 (.NET/Avalonia, beta) · [MIT](LICENSE)

![Panel de Al Día](docs/img/es-panel.jpg)

## ¿Para quién es?

Para cualquier autónomo en España que quiera tener sus números en orden sin pagar un programa de contabilidad completo. Está pensada para seguir siendo sencilla tanto si haces una factura al mes como treinta, a un cliente o a diez.

Mientras VeriFactu no sea obligatorio, Al Día puede **emitir tus facturas en PDF** y enviarlas por correo a tus clientes. **No** es un sistema VeriFactu ni envía nada a Hacienda: cuando VeriFactu se aplique, preparas la factura en Al Día y la emites con la **aplicación gratuita VERI\*FACTU de la AEAT**; Al Día la controla y hace las cuentas fiscales a su alrededor.

## Qué hace

- **Panel** con el próximo plazo, qué modelos están presentados, los borradores pendientes y las cifras del trimestre y del año.
- **Ingresos**: facturas con líneas, IVA y retención de IRPF, estado de cobro y PDF adjunto.
  - Los **borradores** preparados en Al Día no cuentan para impuestos hasta que los marcas como emitidos.
  - **Emitir con Al Día**: un PDF A4 limpio con todos los datos obligatorios (NIF y domicilio de emisor y cliente, líneas, IVA, retención, total, IBAN y pie propio), archivado en `~/Documents/Al Día/Facturas/<año>` y listo para **enviarlo por correo** al cliente desde tu app de correo.
  - **Emitir en la AEAT**: cada dato que pide la app de la AEAT, en el mismo orden, con botón de copiar.
  - **Importación de PDF** de las facturas que ya tienes (hechas con una plantilla de hoja de cálculo), con revisión previa.
  - **Numeración anual** (`2027-001`, `2027-002`…, con prefijo opcional) que vuelve a empezar cada 1 de enero y nunca se repite, hagas las facturas que hagas al mes.
- **Gastos**: IVA deducible y gasto de IRPF calculados en cada compra, con valores habituales por categoría (50 % de IVA en vehículo de uso mixto, sin IVA en la cuota de autónomos…).
  - **Importa un ticket o factura** desde su PDF o una foto (o arrástralo a la lista): proveedor, NIF, número, fecha, base, IVA y total se leen con el reconocimiento de texto del propio Mac (Apple Vision) y quedan rellenos para que los revises. También comprueba si la factura lleva tu NIF, es decir, si su IVA es deducible.
- **Modelos fiscales**: borrador del **303** (IVA) y del **130** (pago fraccionado de IRPF) con sus casillas; detecta cuándo no tienes que presentar el 130 (≥ 70 % de ingresos con retención); registra lo presentado con su justificante y arrastra el IVA a compensar.
- **Clientes y servicios**, exportables en el formato de texto exacto que importa la app de la AEAT.
- **Exportación CSV** de los libros de ingresos y gastos para tu gestor.
- Interfaz en **castellano e inglés** (según el idioma del Mac).
- **Local y privada**: todo se queda en tu Mac, sin cuentas. La única conexión es opcional: al escribir un código postal se pregunta la población al servicio de mapas de Apple (solo se envía el código postal).
- **Campos que se rellenan solos**: provincia por el código postal, IBAN agrupado de 4 en 4 y comprobado (ISO 13616), y cláusula de protección de datos recomendada con tus datos.

| Ingresos | Emitir en la AEAT | Modelos fiscales | Ajustes |
|---|---|---|---|
| ![Ingresos](docs/img/es-ingresos.jpg) | ![Ficha de emisión](docs/img/es-emitir.jpg) | ![Modelos fiscales](docs/img/es-modelos.jpg) | ![Ajustes](docs/img/es-ajustes.jpg) |

## Cómo encaja con VeriFactu

Todo autónomo que facture con un programa tendrá que usar un sistema que cumpla VeriFactu ([RD 1007/2023](https://www.boe.es/buscar/act.php?id=BOE-A-2023-24840)). La fecha legal es el **1 de julio de 2027** (RDL 15/2025); en octubre de 2026 Hacienda anunció un nuevo aplazamiento a **octubre de 2028**, pendiente de publicarse. Hasta entonces Al Día emite facturas en PDF como lo haría una hoja de cálculo, y avisa si la fecha de una factura es posterior al plazo (`VeriFactu.obligatorioDesde` en `FacturaPDF.swift`). Programar software VeriFactu convierte a quien lo hace en su «productor» ante Hacienda, así que a partir de entonces Al Día se queda deliberadamente en el lado seguro:

1. **Preparas** la factura en Al Día (borrador).
2. **La emites** en la [aplicación gratuita VERI\*FACTU](https://sede.agenciatributaria.gob.es/Sede/procedimientoini/IZ86.shtml) copiando los datos de la ficha.
3. **La marcas como emitida** en Al Día con el número oficial y el PDF (con su código QR).

Antes de la primera factura, exporta tus clientes y servicios desde Al Día e impórtalos en la app de la AEAT (*Otros servicios → Clientes / Productos → Importar*): así emitir es solo elegirlos.

## Instalación

**Descarga:** baja el último `.zip` de [Releases](https://github.com/juanitreque/al-dia/releases), descomprímelo y arrastra **Al Día.app** a Aplicaciones. No está notarizada, así que la primera vez abre **Ajustes del Sistema → Privacidad y seguridad → Abrir igualmente**.

**O compílala desde el código** con un solo comando:

```bash
git clone https://github.com/juanitreque/al-dia.git
cd al-dia/mac
scripts/build-app.sh --install
```

Necesitas macOS 15 o posterior y Xcode 16 o posterior (hace falta el toolchain de Xcode para las macros de SwiftData y el catálogo de textos).

La app queda en la carpeta Aplicaciones (`/Applications`) como **Al Día.app**. Está firmada sin certificado de desarrollador, así que la primera vez macOS puede decir que no puede verificarla: clic derecho sobre la app → **Abrir** → **Abrir**.

### Windows (beta)

Descarga **`AlDia-Windows-…-instalador.exe`** desde [Releases](https://github.com/juanitreque/al-dia/releases) (busca *Al Día para Windows*) y ábrelo. El instalador no está firmado, así que Windows muestra «Windows protegió su PC»: pulsa **Más información → Ejecutar de todas formas**. Se instala solo para tu usuario, sin contraseña de administrador. También hay un zip *portable* que no necesita instalación.

La versión de Windows comparte la misma lógica fiscal y los mismos casos de prueba que la de Mac. En esta beta la interfaz está solo en castellano, las fotos de tickets se adjuntan pero no se leen (los PDF sí) y los correos se preparan como borrador `.eml` para tu programa de correo. Los datos se guardan en `%APPDATA%\AlDia` y las facturas emitidas se copian en `Documentos\Al Día`. Menú Inicio → *Al Día (demostración)* la abre con datos de ejemplo.

Para compilarla tú (Windows, macOS o Linux con el SDK de .NET 10): `cd windows && dotnet test && dotnet run --project src/AlDia -- --demo`. El instalador lo genera GitHub Actions (`.github/workflows/windows.yml`) con Inno Setup al subir una etiqueta `windows-v*`.

## Tus datos

Todo se guarda en `~/Library/Application Support/AlDia/` (una base de datos SwiftData/SQLite y los PDF adjuntos) y entra en las copias de Time Machine. *Ajustes → Mostrar en Finder* abre la carpeta. Nada sale de tu Mac.

## Desarrollo

```bash
cd mac
swift test                        # tests de la lógica fiscal (AlDiaCore)
swift run AlDia --demo            # arranca con una base temporal llena de datos de ejemplo
scripts/sync-strings.sh           # extrae los textos a packaging/Localizable.xcstrings
swift scripts/make-icon.swift     # regenera el icono
```

```
mac/                app de macOS (SwiftUI + SwiftData)
  Sources/
    AlDiaCore/   lógica pura: trimestres, plazos, modelos 303/130, numeración, lectores de facturas y tickets, formatos AEAT
    AlDia/       app SwiftUI: modelos SwiftData, vistas, datos de demostración
  Tests/         tests (Swift Testing) de AlDiaCore
  packaging/     Info.plist, icono y Localizable.xcstrings (castellano de origen, traducción al inglés)
  scripts/       compilación, sincronización de textos e icono
windows/            app de Windows (.NET 10 + Avalonia, SQLite, QuestPDF)
  src/AlDia.Core/   la misma lógica fiscal portada a C#
  src/AlDia/        la app: datos, PDF de factura, paquete del gestor, pantallas
  tests/            los mismos casos de prueba (xUnit): dotnet test
  installer/        script de Inno Setup y notas de la versión
```

El castellano es el idioma de origen del código y del catálogo de textos; el inglés está en `packaging/Localizable.xcstrings` (se edita con Xcode). Las contribuciones son bienvenidas, sobre todo correcciones de la lógica fiscal, que deben venir con su test.

## Limitaciones

- Solo régimen general de IVA y estimación directa en IRPF. No contempla recargo de equivalencia, módulos, operaciones intracomunitarias ni tablas de amortización.
- Los plazos pasan al lunes si caen en fin de semana, pero no tienen en cuenta festivos nacionales ni autonómicos.
- La lectura de tickets es una ayuda: revisa siempre los importes antes de guardar. Los tickets escritos a mano o muy borrosos pueden necesitar que los teclees.
- El importador de PDF de *tus propias facturas antiguas* entiende un formato de factura concreto (cabecera `Número: … Fecha: …` y tabla `CONCEPTO / UNIDAD / PRECIO`); otros formatos requieren cambios en el código.
- Los importes se muestran siempre en formato español (1.234,56 €).

## Aviso

Al Día es una herramienta personal que se comparte tal cual. No tiene relación con la Agencia Tributaria y **no es asesoramiento fiscal**: las cifras son una ayuda para revisar; compruébalas siempre con el formulario oficial o con tu gestor antes de presentar.

## Licencia

[MIT](LICENSE) © juanitreque
