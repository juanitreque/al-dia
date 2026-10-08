using System.Text;
using UglyToad.PdfPig;

namespace AlDia.Servicios;

/// <summary>Texto de un PDF reconstruido por líneas visuales (palabras a la misma altura, de izquierda a derecha).</summary>
public static class LecturaPdf
{
    public static string Texto(byte[] pdf)
    {
        using var documento = PdfDocument.Open(pdf);
        var sb = new StringBuilder();
        foreach (var pagina in documento.GetPages())
        {
            var lineas = new List<(double Y, List<(double X, string Texto)> Palabras)>();
            foreach (var palabra in pagina.GetWords())
            {
                var y = palabra.BoundingBox.Bottom;
                var linea = lineas.FirstOrDefault(l => Math.Abs(l.Y - y) < 2.5);
                if (linea.Palabras is null) lineas.Add((y, [(palabra.BoundingBox.Left, palabra.Text)]));
                else linea.Palabras.Add((palabra.BoundingBox.Left, palabra.Text));
            }
            foreach (var (_, palabras) in lineas.OrderByDescending(l => l.Y))
                sb.AppendLine(string.Join(" ", palabras.OrderBy(p => p.X).Select(p => p.Texto)));
        }
        return sb.ToString();
    }
}
