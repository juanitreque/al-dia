using AlDia.Servicios;
using Avalonia.Controls;

namespace AlDia.Vistas;

/// <summary>Prepara un borrador de correo con el adjunto y muestra los datos para copiarlos
/// por si el programa de correo no los recoge.</summary>
public static class DialogoCorreo
{
    public static async Task Enviar(Window dueño, string para, string asunto, string cuerpo, string adjunto)
    {
        string? error = null;
        try { Sistema.BorradorCorreo(para, asunto, cuerpo, adjunto); }
        catch (Exception e) { error = e.Message; }

        var contenido = new StackPanel { Spacing = 10 };
        contenido.Children.Add(Ui.Nota(error is null
            ? "Se ha abierto un correo nuevo con el archivo adjunto en tu programa de correo (Outlook, Thunderbird…). Revísalo y pulsa Enviar."
            : $"No se pudo abrir el programa de correo ({error}).", error is null ? null : Ui.Error));
        contenido.Children.Add(Ui.Nota("Si tu programa no lo abre o no rellena los datos, cópialos desde aquí y adjunta el archivo a mano:"));
        foreach (var (titulo, valor) in new[] { ("Para", para), ("Asunto", asunto) })
        {
            var caja = Ui.Texto(valor, ancho: 330);
            caja.IsReadOnly = true;
            contenido.Children.Add(Ui.Fila(new TextBlock { Text = titulo, Width = 60, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center },
                                           caja, Ui.Boton("Copiar", () => Ui.Copiar(caja, valor))));
        }
        contenido.Children.Add(Ui.Fila(
            Ui.Boton("Copiar texto del mensaje", () => Ui.Copiar(contenido, cuerpo)),
            Ui.Boton("Mostrar el archivo", () => Sistema.MostrarEnCarpeta(adjunto))));
        await Ui.Dialogo(dueño, "Enviar por correo", contenido, "Hecho", ancho: 560);
    }
}
