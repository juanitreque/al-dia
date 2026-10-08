using AlDia.Datos;
using AlDia.Servicios;
using Avalonia;

namespace AlDia;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        Demo.Activo = args.Contains("--demo");
        if (Demo.Activo)
        {
            if (Directory.Exists(Demo.Carpeta)) Directory.Delete(Demo.Carpeta, recursive: true);
            Almacen.Abrir(Demo.Carpeta);
            Sistema.CarpetaDocumentos = Path.Combine(Demo.Carpeta, "Documentos");
            Demo.Rellenar();
        }
        else
        {
            Almacen.Abrir(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AlDia"));
        }
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}
