using AlDia.Core;
using AlDia.Datos;
using AlDia.Servicios;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace AlDia.Vistas;

public sealed class AjustesVista : Seccion
{
    public override string Nombre => "Ajustes";

    public override void Refrescar()
    {
        TextBox Campo(string clave, string marca = "", double ancho = double.NaN) => Ui.Texto(Ajustes.Texto(clave), marca, ancho);

        var nombre = Campo(Ajustes.Nombre, "Nombre y apellidos");
        var nif = Campo(Ajustes.Nif, "00000000T", 160);
        var direccion = Campo(Ajustes.Direccion, "C/ Mayor, 1, 2º A");
        var (cp, poblacion, provincia) = ClientesVista.CamposLocalidad(Ajustes.Texto(Ajustes.CodigoPostal),
            Ajustes.Texto(Ajustes.Poblacion), Ajustes.Texto(Ajustes.Provincia));
        var email = Campo(Ajustes.Email, "tu@correo.es");
        var telefono = Campo(Ajustes.Telefono, "", 160);

        var iban = Ui.Texto(IBAN.Formatear(Ajustes.Texto(Ajustes.Iban)), "ES00 0000 0000 0000 0000 0000", 320);
        var estadoIban = Ui.Nota("");
        void ComprobarIban()
        {
            var limpio = IBAN.Normalizar(iban.Text ?? "");
            estadoIban.Text = limpio.Length == 0 ? "" : IBAN.EsValido(limpio) ? "✓ IBAN válido" : "IBAN no válido: revisa los dígitos";
            estadoIban.Foreground = IBAN.EsValido(limpio) ? Brushes.SeaGreen : Ui.Error;
        }
        iban.TextChanged += (_, _) =>
        {
            var formateado = IBAN.Formatear(iban.Text ?? "");
            if (formateado != iban.Text)
            {
                iban.Text = formateado;
                iban.CaretIndex = formateado.Length;
            }
            ComprobarIban();
        };
        ComprobarIban();

        var pie = new TextBox { Text = Ajustes.Texto(Ajustes.PieFactura), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 110 };
        var rgpd = Ui.Boton("Usar la cláusula de protección de datos recomendada", () => pie.Text = PieFactura.ClausulaRGPD);

        var iva = Ui.Opciones(["0 %", "4 %", "10 %", "21 %"], Formato.Porcentaje(Ajustes.IvaPorDefecto), 110);
        var retenciones = new[] { "15 % (general)", "7 % (primeros 3 años de actividad)", "Sin retención" };
        var retencion = Ui.Opciones(retenciones, Ajustes.RetencionPorDefecto switch { 7 => retenciones[1], 0 => retenciones[2], _ => retenciones[0] }, 300);
        var gestor = Campo(Ajustes.EmailGestor, "gestoria@ejemplo.es");

        var formatos = new[] { "Anual: 2027-001, 2027-002…", "Continuar el de la última factura" };
        var formato = Ui.Opciones(formatos, Almacen.Ajuste(Ajustes.FormatoNumeracion) == "continuar" ? formatos[1] : formatos[0], 300);
        var prefijo = Campo(Ajustes.PrefijoNumeracion, "F", 80);

        var guardado = Ui.Nota("");
        void Guardar()
        {
            Almacen.Ajuste(Ajustes.Nombre, nombre.Text ?? "");
            Almacen.Ajuste(Ajustes.Nif, (nif.Text ?? "").ToUpperInvariant());
            Almacen.Ajuste(Ajustes.Direccion, direccion.Text ?? "");
            Almacen.Ajuste(Ajustes.CodigoPostal, cp.Text ?? "");
            Almacen.Ajuste(Ajustes.Poblacion, poblacion.Text ?? "");
            Almacen.Ajuste(Ajustes.Provincia, provincia.Text ?? "");
            Almacen.Ajuste(Ajustes.Email, email.Text ?? "");
            Almacen.Ajuste(Ajustes.Telefono, telefono.Text ?? "");
            Almacen.Ajuste(Ajustes.Iban, IBAN.Normalizar(iban.Text ?? ""));
            Almacen.Ajuste(Ajustes.PieFactura, pie.Text ?? "");
            Almacen.Ajuste(Ajustes.IvaDefecto, ((string)iva.SelectedItem!).Replace(" %", ""));
            Almacen.Ajuste(Ajustes.RetencionDefecto, retencion.SelectedIndex switch { 1 => "7", 2 => "0", _ => "15" });
            Almacen.Ajuste(Ajustes.EmailGestor, gestor.Text ?? "");
            Almacen.Ajuste(Ajustes.FormatoNumeracion, formato.SelectedIndex == 1 ? "continuar" : "anual");
            Almacen.Ajuste(Ajustes.PrefijoNumeracion, prefijo.Text ?? "");
            guardado.Text = $"Guardado a las {DateTime.Now:HH:mm}.";
        }

        var p = new StackPanel { Spacing = 8, MaxWidth = 760, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
        p.Children.Add(Ui.Titulo("Ajustes"));
        p.Children.Add(Ui.Subtitulo("Tus datos"));
        p.Children.Add(Ui.Formulario(("Nombre", nombre), ("NIF", nif), ("Domicilio fiscal", direccion), ("Código postal", cp),
            ("Población", poblacion), ("Provincia", provincia), ("Email", email), ("Teléfono", telefono)));
        p.Children.Add(Ui.Subtitulo("Facturas que emite Al Día"));
        p.Children.Add(Ui.Formulario(("IBAN para el cobro", Ui.Fila(iban, estadoIban)), ("Texto al pie (opcional)", pie)));
        p.Children.Add(rgpd);
        p.Children.Add(Ui.Nota("Aparecen en el PDF de las facturas. En el pie, {nombre}, {nif}, {domicilio} y {email} se sustituyen por tus datos."));
        p.Children.Add(Ui.Subtitulo("Valores por defecto en facturas nuevas"));
        p.Children.Add(Ui.Formulario(("IVA", iva), ("Retención IRPF", retencion)));
        p.Children.Add(Ui.Subtitulo("Numeración de facturas"));
        p.Children.Add(Ui.Formulario(("Formato", formato), ("Prefijo (opcional)", prefijo)));
        p.Children.Add(Ui.Nota("La numeración anual vuelve a 001 cada 1 de enero."));
        p.Children.Add(Ui.Subtitulo("Gestoría"));
        p.Children.Add(Ui.Formulario(("Email del gestor", gestor)));
        p.Children.Add(Ui.Subtitulo("Tus datos en este ordenador"));
        p.Children.Add(Ui.Nota($"Base de datos y documentos adjuntos: {Almacen.Carpeta}\nCopias de las facturas emitidas: {Sistema.CarpetaDocumentos}\nHaz copia de seguridad de ambas carpetas (OneDrive, disco externo…)."));
        p.Children.Add(Ui.Barra(Ui.Boton("Abrir carpeta de datos", () => Sistema.Abrir(Almacen.Carpeta)),
                                Ui.Boton("Abrir carpeta de facturas", () => { Directory.CreateDirectory(Sistema.CarpetaDocumentos); Sistema.Abrir(Sistema.CarpetaDocumentos); })));
        var botonGuardar = Ui.Boton("Guardar ajustes", Guardar, principal: true);
        p.Children.Add(Ui.Fila(botonGuardar, guardado));
        p.Children.Add(Ui.Nota($"Al Día {typeof(AjustesVista).Assembly.GetName().Version?.ToString(3)} · código abierto (MIT) · github.com/juanitreque/al-dia"));
        p.Margin = new Thickness(0, 0, 0, 30);
        Content = new ScrollViewer { Content = p };
    }
}
