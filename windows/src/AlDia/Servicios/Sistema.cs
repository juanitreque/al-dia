using System.Diagnostics;
using System.Text;

namespace AlDia.Servicios;

/// <summary>Integración con el sistema: abrir archivos, mostrarlos en el Explorador y borradores de correo.</summary>
public static class Sistema
{
    /// <summary>Carpeta de trabajo visible: Documentos\Al Día (en modo demo, una carpeta temporal).</summary>
    public static string CarpetaDocumentos { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Al Día");

    public static string Temporal(string nombre)
    {
        var carpeta = Path.Combine(Path.GetTempPath(), "AlDia", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(carpeta);
        return Path.Combine(carpeta, nombre);
    }

    /// <summary>Abre el archivo o la carpeta con el programa predeterminado.</summary>
    public static void Abrir(string ruta) => Process.Start(new ProcessStartInfo(ruta) { UseShellExecute = true });

    public static void MostrarEnCarpeta(string ruta)
    {
        if (OperatingSystem.IsWindows()) Process.Start("explorer.exe", $"/select,\"{ruta}\"");
        else if (OperatingSystem.IsMacOS()) Process.Start("open", ["-R", ruta]);
        else Abrir(Path.GetDirectoryName(ruta)!);
    }

    /// <summary>Crea un borrador de correo (.eml marcado como no enviado) con el adjunto y lo abre:
    /// Outlook y la mayoría de programas de correo lo muestran listo para pulsar Enviar.</summary>
    public static string BorradorCorreo(string para, string asunto, string cuerpo, string adjunto, bool abrir = true)
    {
        var limite = "----=_AlDia_" + Guid.NewGuid().ToString("N");
        var nombre = Path.GetFileName(adjunto);
        var tipo = Path.GetExtension(adjunto).ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".zip" => "application/zip",
            _ => "application/octet-stream",
        };
        var sb = new StringBuilder();
        sb.Append("X-Unsent: 1\r\n");
        if (para.Length > 0) sb.Append($"To: {para}\r\n");
        sb.Append($"Subject: {Codificar(asunto)}\r\n");
        sb.Append("MIME-Version: 1.0\r\n");
        sb.Append($"Content-Type: multipart/mixed; boundary=\"{limite}\"\r\n\r\n");
        sb.Append($"--{limite}\r\n");
        sb.Append("Content-Type: text/plain; charset=utf-8\r\nContent-Transfer-Encoding: base64\r\n\r\n");
        sb.Append(Base64EnLineas(Encoding.UTF8.GetBytes(cuerpo.Replace("\r\n", "\n").Replace("\n", "\r\n"))));
        sb.Append($"--{limite}\r\n");
        sb.Append($"Content-Type: {tipo}; name=\"{Codificar(nombre)}\"\r\n");
        sb.Append("Content-Transfer-Encoding: base64\r\n");
        sb.Append($"Content-Disposition: attachment; filename=\"{Codificar(nombre)}\"\r\n\r\n");
        sb.Append(Base64EnLineas(File.ReadAllBytes(adjunto)));
        sb.Append($"--{limite}--\r\n");

        var eml = Path.Combine(Path.GetDirectoryName(adjunto)!, Path.GetFileNameWithoutExtension(adjunto) + ".eml");
        File.WriteAllText(eml, sb.ToString(), new UTF8Encoding(false));
        if (abrir) Abrir(eml);
        return eml;
    }

    private static string Codificar(string texto) =>
        texto.All(c => c < 128) ? texto : $"=?utf-8?B?{Convert.ToBase64String(Encoding.UTF8.GetBytes(texto))}?=";

    private static string Base64EnLineas(byte[] datos)
    {
        var b64 = Convert.ToBase64String(datos);
        var sb = new StringBuilder();
        for (var i = 0; i < b64.Length; i += 76) sb.Append(b64, i, Math.Min(76, b64.Length - i)).Append("\r\n");
        return sb.ToString();
    }
}
