**Al Día para Windows 0.2.1 (beta).** La misma lógica fiscal que la versión de Mac, con las mismas pruebas.

### Novedades de la 0.2.1
- **Aviso de factura electrónica obligatoria.** Desde el **6 de octubre de 2028**, las facturas a empresas y autónomos tendrán que ser electrónicas (formato estructurado, por la solución pública gratuita de la AEAT o una plataforma privada) para quien facture 8 millones de euros o menos al año ([RD 238/2026](https://www.boe.es/buscar/doc.php?id=BOE-A-2026-7295) y [Orden HAC/1028/2026](https://www.boe.es/diario_boe/txt.php?id=BOE-A-2026-20587)). Al Día avisa al emitir en PDF una factura posterior a esa fecha. Las facturas a particulares no cambian.
- **VeriFactu, situación actual.** La fecha legal sigue siendo el **1 de julio de 2027**. El 5 de octubre de 2026 Hacienda [anunció](https://www.hacienda.gob.es/sgt/gabsehacienda/nota-informativa-verifactu.pdf) que la aplazará a octubre de 2028 para que coincida con la factura electrónica, pero aún no está en el BOE; el aviso de la app ya lo indica.

### Novedades de la 0.2.0
- **Nueva factura en PDF**, con el mismo diseño que la versión de Mac. Los conceptos largos se reparten en varias líneas, las facturas con muchas líneas continúan en otra página (repitiendo la cabecera de la tabla y el pie) y la marca «BORRADOR» de la vista previa ya no se corta. Los PDF ocupan unos 45 KB.
- **Nueva licencia: GNU GPL v3.0 o posterior.** El código es libre y puedes compilarlo tú mismo: <https://github.com/juanitreque/al-dia>. Todos los componentes de terceros tienen licencias libres compatibles (ver `NOTICE.md` en el repositorio).
- La versión portable se descomprime en una sola carpeta «Al Dia», sin archivos de depuración.

### Instalar
1. Descarga **`AlDia-Windows-…-instalador.exe`** y ábrelo.
2. Windows mostrará «Windows protegió su PC» porque el instalador no está firmado: pulsa **Más información → Ejecutar de todas formas**.
3. Se instala solo para tu usuario, sin pedir contraseña de administrador. Al terminar, abre Al Día desde el menú Inicio.

Si ya tenías una versión anterior, instala esta encima: tus datos se conservan.

¿Quieres probar sin tus datos? Abre **Al Día (demostración)** en el menú Inicio: arranca con datos ficticios que se borran al cerrar.

La versión **portable** (`…-portable.zip`) no necesita instalación: descomprímela (clic derecho → Extraer todo), entra en la carpeta «Al Dia» y abre `AlDia.exe`.

### Qué incluye
- Panel con el próximo plazo y el resumen del trimestre y del año.
- Ingresos: facturas con líneas y servicios del catálogo, numeración anual, **emisión en PDF**, envío por correo, cobros, importación de facturas antiguas en PDF.
- Gastos: importación de facturas de compra en PDF con lectura automática de proveedor, NIF, fecha e importes.
- Modelos 303 y 130 calculados con sus casillas, registro de modelos presentados y **paquete ZIP para el gestor**.
- Clientes (el código postal rellena la provincia), servicios, exportación a la app de la AEAT, IBAN validado y pie de factura con la cláusula RGPD.

### Limitaciones de esta beta
- Interfaz solo en castellano.
- Las fotos de tickets se adjuntan pero no se leen automáticamente (los PDF sí).
- El correo se prepara como borrador (.eml) que abre tu programa de correo; si no lo abre, la app te da los datos para copiarlos.

Tus datos se guardan en `%APPDATA%\AlDia` y las facturas emitidas en `Documentos\Al Día`. Desinstalar no los borra.

---

**Al Día for Windows 0.2.1 (beta).** Warns when issuing a PDF invoice dated on or after 6 October 2028, when B2B e-invoicing becomes mandatory for businesses invoicing €8M a year or less (invoices to consumers are not affected); VeriFactu status updated (legal date still 1 July 2027, delay to October 2028 announced but not yet in the BOE). Also includes 0.2.0: new invoice PDF matching the Mac design and the **GNU GPL v3.0 or later** license; source code: <https://github.com/juanitreque/al-dia>. Download the installer and click *More info → Run anyway* on the SmartScreen warning (the installer is not signed). Spanish-only UI for now.
