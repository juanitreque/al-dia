using System.IO.Compression;
using System.Text;
using AlDia.Core;
using AlDia.Datos;

namespace AlDia.Servicios;

// MARK: Libros en CSV (Excel en español: separador ";" y coma decimal)

public static class Libros
{
    public static string Ingresos(IEnumerable<Ingreso> lista)
    {
        var filas = new List<string[]>
        {
            new[] { "Fecha expedición", "Número", "NIF destinatario", "Destinatario", "Concepto", "Base imponible", "Tipo IVA",
                    "Cuota IVA", "Tipo retención", "Retención", "Total", "Cobrada", "Fecha cobro" },
        };
        foreach (var i in lista.OrderBy(i => i.Fecha))
            filas.Add([Formato.Fecha(i.Fecha), i.Numero, i.Cliente?.Nif ?? "", i.Cliente?.Nombre ?? "", i.Concepto,
                       Imp(i.Base), Imp(i.TipoIVA), Imp(i.CuotaIVA), Imp(i.TipoRetencion), Imp(i.Retencion), Imp(i.Total),
                       i.Cobrada ? "Sí" : "No", i.FechaCobro is { } f ? Formato.Fecha(f) : ""]);
        return Csv(filas);
    }

    public static string Gastos(IEnumerable<Gasto> lista)
    {
        var filas = new List<string[]>
        {
            new[] { "Fecha", "Nº factura", "NIF proveedor", "Proveedor", "Concepto", "Categoría", "Base imponible", "Tipo IVA",
                    "Cuota IVA", "% deducible", "IVA deducible", "Gasto IRPF", "Factura completa", "Bien de inversión" },
        };
        foreach (var g in lista.OrderBy(g => g.Fecha))
        {
            var d = g.Datos;
            filas.Add([Formato.Fecha(g.Fecha), g.NumeroFactura, g.NifProveedor, g.Proveedor, g.Concepto, g.Categoria,
                       Imp(g.Base), Imp(g.TipoIVA), Imp(g.CuotaIVA), Imp(g.PorcentajeDeducibleIVA), Imp(d.IvaDeducible),
                       Imp(d.GastoIRPF), g.FacturaCompleta ? "Sí" : "No", g.BienInversion ? "Sí" : "No"]);
        }
        return Csv(filas);
    }

    private static string Imp(decimal v) => v.ToString("0.00", Formato.Es);

    /// <summary>CSV con BOM (Excel detecta UTF-8) y separador ";".</summary>
    public static string Csv(IEnumerable<string[]> filas) =>
        "﻿" + string.Join("\r\n", filas.Select(f => string.Join(";", f.Select(Campo)))) + "\r\n";

    private static string Campo(string s) =>
        s.IndexOfAny([';', '"', '\n', '\r']) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
}

// MARK: Paquete trimestral

/// <summary>Zip trimestral para la gestoría: resumen, libros en CSV, PDF de las facturas,
/// documentos de los gastos y justificantes de los modelos presentados.</summary>
public sealed class PaqueteGestor
{
    public Trimestre Trimestre { get; }
    public List<Ingreso> IngresosTrimestre { get; }
    public List<Gasto> GastosTrimestre { get; }
    public List<ModeloPresentado> Presentados { get; }
    private readonly List<Ingreso> todosIngresos;
    private readonly List<Gasto> todosGastos;
    private readonly List<ModeloPresentado> modelos;

    public PaqueteGestor(Trimestre t, IEnumerable<Ingreso> ingresos, IEnumerable<Gasto> gastos, IEnumerable<ModeloPresentado> modelos)
    {
        Trimestre = t;
        todosIngresos = ingresos.Where(i => !i.Borrador).ToList();
        todosGastos = gastos.ToList();
        this.modelos = modelos.ToList();
        IngresosTrimestre = todosIngresos.Where(i => t.Contiene(i.Fecha)).OrderBy(i => i.Fecha).ToList();
        GastosTrimestre = todosGastos.Where(g => t.Contiene(g.Fecha)).OrderBy(g => g.Fecha).ToList();
        Presentados = this.modelos.Where(m => m.Ejercicio == t.Ejercicio && m.Periodo == t.Periodo).ToList();
    }

    /// <summary>"Nombre Apellido - 3T 2026": el gestor reconoce de quién es y de qué periodo.</summary>
    public string Nombre
    {
        get
        {
            var autonomo = Ajustes.Texto(Ajustes.Nombre);
            return autonomo.Length == 0 ? $"Documentación {Trimestre.Etiqueta}" : $"{autonomo} - {Trimestre.Etiqueta}";
        }
    }

    public List<Gasto> GastosSinDocumento => GastosTrimestre.Where(g => !g.TieneDocumento).ToList();

    /// <summary>Crea el zip en una carpeta temporal y devuelve su ruta.</summary>
    public string Crear(bool incluirDocumentos)
    {
        var raiz = Path.GetDirectoryName(Sistema.Temporal("x"))!;
        var carpeta = Path.Combine(raiz, Formato.NombreArchivo(Nombre));
        Directory.CreateDirectory(carpeta);
        var t = Trimestre.Etiqueta;
        var bom = new UTF8Encoding(false);
        File.WriteAllText(Path.Combine(carpeta, $"Resumen {t}.txt"), Resumen().Replace("\n", "\r\n"), new UTF8Encoding(true));
        File.WriteAllText(Path.Combine(carpeta, $"Ingresos {t}.csv"), Libros.Ingresos(IngresosTrimestre), bom);
        File.WriteAllText(Path.Combine(carpeta, $"Gastos {t}.csv"), Libros.Gastos(GastosTrimestre), bom);

        if (incluirDocumentos)
        {
            var facturas = Path.Combine(carpeta, "Facturas emitidas");
            Directory.CreateDirectory(facturas);
            foreach (var i in IngresosTrimestre)
            {
                // El documento guardado (PDF original o el de Al Día); si no hay, se genera
                var datos = i.TieneDocumento ? Almacen.AdjuntoIngreso(i.Id) : null;
                var ext = datos is { Length: > 0 } ? Extension(i.AdjuntoNombre, ".pdf") : ".pdf";
                datos = datos is { Length: > 0 } ? datos : FacturaPdf.Generar(i);
                File.WriteAllBytes(SinRepetir(Path.Combine(facturas, Formato.NombreArchivo($"Factura {i.Numero}") + ext)), datos);
            }

            var conDocumento = GastosTrimestre.Where(g => g.TieneDocumento).ToList();
            if (conDocumento.Count > 0)
            {
                var docs = Path.Combine(carpeta, "Gastos");
                Directory.CreateDirectory(docs);
                foreach (var g in conDocumento)
                {
                    if (Almacen.AdjuntoGasto(g.Id) is not { Length: > 0 } datos) continue;
                    var partes = new[] { g.Fecha.ToString("yyyy-MM-dd"), g.Proveedor, g.NumeroFactura }.Where(s => s.Length > 0);
                    var nombre = Formato.NombreArchivo(string.Join(" - ", partes)) + Extension(g.AdjuntoNombre, "");
                    File.WriteAllBytes(SinRepetir(Path.Combine(docs, nombre)), datos);
                }
            }

            var justificantes = Presentados.Where(m => m.TieneDocumento).ToList();
            if (justificantes.Count > 0)
            {
                var docs = Path.Combine(carpeta, "Modelos presentados");
                Directory.CreateDirectory(docs);
                foreach (var m in justificantes)
                {
                    if (Almacen.AdjuntoModelo(m.Id) is not { Length: > 0 } datos) continue;
                    var nombre = $"Modelo {m.Modelo} {m.Periodo} {m.Ejercicio}" + Extension(m.AdjuntoNombre, "");
                    File.WriteAllBytes(SinRepetir(Path.Combine(docs, Formato.NombreArchivo(nombre))), datos);
                }
            }
        }

        var zip = Path.Combine(raiz, Formato.NombreArchivo(Nombre) + ".zip");
        ZipFile.CreateFromDirectory(carpeta, zip, CompressionLevel.Optimal, includeBaseDirectory: true);
        return zip;
    }

    /// <summary>Resumen en texto: totales, 303 y 130 con casillas, y avisos.</summary>
    public string Resumen()
    {
        var emisor = new Emisor();
        var di = todosIngresos.Select(i => i.Datos).ToList();
        var dg = todosGastos.Select(g => g.Datos).ToList();
        var l303 = new Liquidacion303(Trimestre, di, dg);
        var l130 = new Liquidacion130(Trimestre, di, dg, modelos.Pagos130Anteriores(Trimestre));
        var pendiente = modelos.PendienteCompensar303(Trimestre);
        var gt = GastosTrimestre.Select(g => g.Datos).ToList();
        var rango = $"{Formato.Fecha(Trimestre.Inicio)} – {Formato.Fecha(Trimestre.Fin.AddDays(-1))}";

        var sb = new StringBuilder();
        void Linea(string texto = "") => sb.Append(texto).Append('\n');
        void Cifra(string titulo, decimal valor)
        {
            var izq = "  " + titulo;
            var imp = Formato.Euros(valor);
            Linea(izq + new string(' ', Math.Max(2, 64 - izq.Length - imp.Length)) + imp);
        }

        Linea($"RESUMEN {Trimestre.Etiqueta} ({rango})");
        Linea($"{emisor.Nombre} · NIF {emisor.Nif}");
        Linea($"Preparado el {Formato.Fecha(DateOnly.FromDateTime(DateTime.Today))}. Cifras orientativas: revisar antes de presentar.");
        Linea();
        Linea($"INGRESOS (facturas emitidas): {IngresosTrimestre.Count}");
        Cifra("Base imponible", IngresosTrimestre.Sum(i => i.Base));
        Cifra("IVA repercutido", IngresosTrimestre.Sum(i => i.CuotaIVA));
        Cifra("Retenciones IRPF", IngresosTrimestre.Sum(i => i.Retencion));
        Cifra("Total facturado", IngresosTrimestre.Sum(i => i.Total));
        Linea();
        Linea($"GASTOS: {GastosTrimestre.Count}");
        Cifra("Base imponible", GastosTrimestre.Sum(g => g.Base));
        Cifra("IVA soportado", GastosTrimestre.Sum(g => g.CuotaIVA));
        Cifra("IVA deducible", gt.Sum(g => g.IvaDeducible));
        Cifra("Gasto deducible IRPF", gt.Sum(g => g.GastoIRPF));
        Linea();
        Linea("MODELO 303 · IVA");
        foreach (var d in l303.Devengos)
            if (d.Casillas is { } c)
            {
                Cifra($"[{c.Base}] Base imponible al {Formato.Porcentaje(d.Tipo)}", d.Base);
                Cifra($"[{c.Cuota}] Cuota devengada al {Formato.Porcentaje(d.Tipo)}", d.Cuota);
            }
        Cifra("[27] Total cuota devengada", l303.TotalDevengado);
        Cifra("[28] Base operaciones interiores corrientes", l303.BaseCorrientes);
        Cifra("[29] Cuota deducible operaciones corrientes", l303.CuotaCorrientes);
        if (l303.CuotaInversion > 0)
        {
            Cifra("[30] Base bienes de inversión", l303.BaseInversion);
            Cifra("[31] Cuota deducible bienes de inversión", l303.CuotaInversion);
        }
        Cifra("[45] Total a deducir", l303.TotalDeducir);
        Cifra("[46] Resultado régimen general", l303.Resultado);
        if (pendiente > 0)
        {
            Cifra("Cuotas a compensar de periodos anteriores", -pendiente);
            Cifra("Resultado de la liquidación", l303.ResultadoCompensando(pendiente));
        }
        Linea();
        Linea("MODELO 130 · Pago fraccionado IRPF");
        Cifra("[01] Ingresos computables (1 ene – fin del trimestre)", l130.Ingresos);
        Cifra("[02] Gastos fiscalmente deducibles", l130.Gastos);
        Cifra("[03] Rendimiento neto", l130.Rendimiento);
        Cifra("[04] 20 % del rendimiento", l130.Cuota);
        Cifra("[05] Pagos fraccionados de trimestres anteriores", -l130.PagosAnteriores);
        Cifra("[06] Retenciones soportadas", -l130.Retenciones);
        Cifra("[07] Pago fraccionado previo", l130.Resultado);
        var pct = Liquidacion130.PorcentajeConRetencion(di, Trimestre.Ejercicio - 1)
                  ?? Liquidacion130.PorcentajeConRetencion(di, Trimestre.Ejercicio);
        if (pct >= 70) Linea($"  {Formato.Porcentaje(pct.Value)} de ingresos con retención: no estás obligado a presentar el 130.");
        if (GastosSinDocumento.Count > 0)
        {
            Linea();
            Linea($"GASTOS SIN DOCUMENTO ADJUNTO: {GastosSinDocumento.Count}");
            foreach (var g in GastosSinDocumento) Linea($"  {Formato.Fecha(g.Fecha)} · {g.Proveedor} · {Formato.Euros(g.Total)}");
        }
        if (Presentados.Count > 0)
        {
            Linea();
            Linea("MODELOS PRESENTADOS");
            foreach (var m in Presentados)
                Linea($"  {m.Modelo} · {Formato.Fecha(m.FechaPresentacion)} · {m.TipoResultado} {Formato.Euros(m.Importe)} · {m.Justificante}");
        }
        return sb.ToString();
    }

    private static string Extension(string? nombre, string porDefecto)
    {
        var ext = Path.GetExtension(nombre ?? "");
        return ext.Length > 0 ? ext.ToLowerInvariant() : porDefecto;
    }

    private static string SinRepetir(string ruta)
    {
        var candidato = ruta;
        for (var n = 2; File.Exists(candidato); n++)
            candidato = Path.Combine(Path.GetDirectoryName(ruta)!, $"{Path.GetFileNameWithoutExtension(ruta)} ({n}){Path.GetExtension(ruta)}");
        return candidato;
    }
}
