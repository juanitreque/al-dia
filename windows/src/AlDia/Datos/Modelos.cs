using AlDia.Core;

namespace AlDia.Datos;

public sealed class Cliente
{
    public long Id { get; set; }
    public string Nombre { get; set; } = "";
    public string Nif { get; set; } = "";
    public string Direccion { get; set; } = "";
    public string CodigoPostal { get; set; } = "";
    public string Poblacion { get; set; } = "";
    public string Provincia { get; set; } = "";
    public string Pais { get; set; } = "ES";
    public string Email { get; set; } = "";
    public string Telefono { get; set; } = "";
    public string Notas { get; set; } = "";

    public ExportacionAEAT.Cliente ParaAEAT =>
        new(Nombre, Nif, Direccion, CodigoPostal, Poblacion, Provincia, Pais, Email, Telefono);

    public override string ToString() => Nombre;
}

/// <summary>Producto o servicio del catálogo (se exporta a la app de la AEAT).</summary>
public sealed class Servicio
{
    public long Id { get; set; }
    public string Codigo { get; set; } = "";
    public string Descripcion { get; set; } = "";
    public decimal Precio { get; set; }
    public decimal TipoIVA { get; set; } = 21;

    public ExportacionAEAT.Producto ParaAEAT => new(Codigo, Descripcion, Precio, TipoIVA);

    public override string ToString() => Codigo.Length > 0 ? $"{Codigo} · {Descripcion}" : Descripcion;
}

public sealed class LineaIngreso
{
    public string Codigo { get; set; } = "";
    public string Concepto { get; set; } = "";
    public decimal Cantidad { get; set; } = 1;
    public decimal Precio { get; set; }

    public decimal Importe => Calculo.Redondear(Cantidad * Precio);
}

/// <summary>Factura de venta: borrador preparado aquí o factura ya emitida.</summary>
public sealed class Ingreso
{
    public long Id { get; set; }
    /// <summary>Un borrador no cuenta para impuestos ni totales.</summary>
    public bool Borrador { get; set; }
    public string Numero { get; set; } = "";
    public DateOnly Fecha { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public long? ClienteId { get; set; }
    public Cliente? Cliente { get; set; }
    public string Concepto { get; set; } = "";
    public decimal Base { get; set; }
    public decimal TipoIVA { get; set; } = 21;
    public decimal CuotaIVA { get; set; }
    public decimal TipoRetencion { get; set; } = 15;
    public decimal Retencion { get; set; }
    public bool Cobrada { get; set; }
    public DateOnly? FechaCobro { get; set; }
    public string Notas { get; set; } = "";
    public byte[]? Adjunto { get; set; }
    public string? AdjuntoNombre { get; set; }
    /// <summary>Hay documento guardado (el contenido se carga aparte, solo cuando hace falta).</summary>
    public bool TieneDocumento { get; set; }
    public List<LineaIngreso> Lineas { get; set; } = [];

    public decimal Total => Base + CuotaIVA - Retencion;
    public string Estado => Borrador ? "Borrador" : "Emitida";
    public DatosIngreso Datos => new(Fecha, Base, TipoIVA, CuotaIVA, Retencion);

    /// <summary>Recalcula base, cuota y retención a partir de las líneas (si las hay) y los tipos.</summary>
    public void Recalcular()
    {
        if (Lineas.Count > 0) Base = Lineas.Sum(l => l.Importe);
        CuotaIVA = Calculo.Porcentaje(TipoIVA, Base);
        Retencion = Calculo.Porcentaje(TipoRetencion, Base);
    }
}

public static class Categorias
{
    public static readonly string[] Todas =
    [
        "Material y equipamiento", "Formación y certificaciones", "Vehículo y desplazamientos", "Teléfono e internet",
        "Cuota de autónomos (RETA)", "Seguros", "Gestoría y asesoría", "Software y suscripciones",
        "Comisiones bancarias", "Otros",
    ];

    /// <summary>Valores habituales al elegir la categoría en un gasto nuevo: (tipo de IVA, % deducible).</summary>
    public static (decimal Iva, decimal Deducible)? Sugerencia(string categoria) => categoria switch
    {
        "Cuota de autónomos (RETA)" or "Seguros" or "Comisiones bancarias" => (0, 100),
        "Vehículo y desplazamientos" => (21, 50),
        _ => null,
    };
}

/// <summary>Factura recibida / gasto de la actividad.</summary>
public sealed class Gasto
{
    public long Id { get; set; }
    public DateOnly Fecha { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    public string Proveedor { get; set; } = "";
    public string NifProveedor { get; set; } = "";
    public string NumeroFactura { get; set; } = "";
    public string Concepto { get; set; } = "";
    public string Categoria { get; set; } = "Otros";
    public decimal Base { get; set; }
    public decimal TipoIVA { get; set; } = 21;
    public decimal CuotaIVA { get; set; }
    public decimal PorcentajeDeducibleIVA { get; set; } = 100;
    public bool FacturaCompleta { get; set; } = true;
    public bool DeducibleIRPF { get; set; } = true;
    public bool BienInversion { get; set; }
    public string Notas { get; set; } = "";
    public byte[]? Adjunto { get; set; }
    public string? AdjuntoNombre { get; set; }
    /// <summary>Hay documento guardado (el contenido se carga aparte, solo cuando hace falta).</summary>
    public bool TieneDocumento { get; set; }

    public decimal Total => Base + CuotaIVA;
    public DatosGasto Datos => new(Fecha, Base, CuotaIVA, PorcentajeDeducibleIVA, FacturaCompleta, DeducibleIRPF, BienInversion);
}

public static class TiposModelo
{
    public static readonly (string Codigo, string Nombre)[] Todos =
    [
        ("303", "303 · IVA trimestral"), ("130", "130 · Pago fraccionado IRPF"), ("390", "390 · Resumen anual IVA"),
        ("347", "347 · Operaciones con terceros"), ("100", "100 · Renta"), ("Otro", "Otro"),
    ];

    public static readonly string[] Resultados = ["A ingresar", "A compensar", "A devolver", "Negativa / cero"];
}

/// <summary>Declaración presentada en la sede de la AEAT.</summary>
public sealed class ModeloPresentado
{
    public long Id { get; set; }
    public string Modelo { get; set; } = "303";
    public int Ejercicio { get; set; } = DateTime.Today.Year;
    /// <summary>"1T"…"4T" o "0A" para anuales.</summary>
    public string Periodo { get; set; } = "1T";
    /// <summary>Importe siempre positivo; el sentido lo da <see cref="TipoResultado"/>.</summary>
    public decimal Importe { get; set; }
    public string TipoResultado { get; set; } = "A ingresar";
    public DateOnly FechaPresentacion { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    /// <summary>NRC del pago o CSV del justificante.</summary>
    public string Justificante { get; set; } = "";
    public string Notas { get; set; } = "";
    public byte[]? Adjunto { get; set; }
    public string? AdjuntoNombre { get; set; }
    /// <summary>Hay documento guardado (el contenido se carga aparte, solo cuando hace falta).</summary>
    public bool TieneDocumento { get; set; }

    public string PeriodoVisible => Periodo == "0A" ? "Anual" : Periodo;
    public bool Corresponde(string modelo, Trimestre t) => Modelo == modelo && Ejercicio == t.Ejercicio && Periodo == t.Periodo;
}

public static class ModelosPresentados
{
    /// <summary>Pagos del 130 ya ingresados en trimestres anteriores del mismo ejercicio (casilla 05).</summary>
    public static decimal Pagos130Anteriores(this IEnumerable<ModeloPresentado> modelos, Trimestre t) =>
        modelos.Where(m => m.Modelo == "130" && m.Ejercicio == t.Ejercicio && m.TipoResultado == "A ingresar"
                           && string.CompareOrdinal(m.Periodo, t.Periodo) < 0)
               .Sum(m => m.Importe);

    /// <summary>IVA que quedó a compensar en el 303 del trimestre anterior.</summary>
    public static decimal PendienteCompensar303(this IEnumerable<ModeloPresentado> modelos, Trimestre t) =>
        modelos.FirstOrDefault(m => m.Corresponde("303", t.Anterior) && m.TipoResultado == "A compensar")?.Importe ?? 0;
}
