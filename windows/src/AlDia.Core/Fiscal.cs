using System.Globalization;
using System.Text.RegularExpressions;

namespace AlDia.Core;

// MARK: Utilidades numéricas

public static class Calculo
{
    /// <summary>Redondeo comercial (mitades hacia fuera) a <paramref name="escala"/> decimales.</summary>
    public static decimal Redondear(decimal valor, int escala = 2) =>
        Math.Round(valor, escala, MidpointRounding.AwayFromZero);

    /// <summary><paramref name="tipo"/> % de <paramref name="baseImponible"/>, redondeado a céntimos.</summary>
    public static decimal Porcentaje(decimal tipo, decimal baseImponible) => Redondear(baseImponible * tipo / 100m);
}

/// <summary>Conversión entre importes y texto en formato español ("1234,56").</summary>
public static class Importe
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");
    private static readonly Regex Numero = new(@"^-?\d+(\.\d+)?\.?$", RegexOptions.CultureInvariant);

    /// <summary>Acepta "45,50", "1.234,56", "45.5", "45 €". Devuelve null si está vacío o no es un número.</summary>
    public static decimal? Parse(string texto)
    {
        var t = texto.Replace("€", "").Replace(" ", "").Replace(" ", "");
        if (t.Contains(',')) t = t.Replace(".", "").Replace(',', '.');
        if (t.Length == 0 || !Numero.IsMatch(t)) return null;
        return decimal.Parse(t.TrimEnd('.'), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                             CultureInfo.InvariantCulture);
    }

    /// <summary>Texto editable sin separador de miles: 1234,5 → "1234,50". El cero se muestra vacío.</summary>
    public static string Texto(decimal valor, int minDecimales = 2, int maxDecimales = 2)
    {
        if (valor == 0) return "";
        var formato = maxDecimales == 0 ? "0" : "0." + new string('0', minDecimales) + new string('#', maxDecimales - minDecimales);
        return valor.ToString(formato, Es);
    }
}

// MARK: Calendario fiscal

public readonly record struct Trimestre : IComparable<Trimestre>
{
    public int Ejercicio { get; }
    public int Numero { get; }

    public Trimestre(int ejercicio, int numero)
    {
        if (numero is < 1 or > 4) throw new ArgumentOutOfRangeException(nameof(numero), "Trimestre fuera de rango");
        Ejercicio = ejercicio;
        Numero = numero;
    }

    public static Trimestre De(DateOnly fecha) => new(fecha.Year, (fecha.Month - 1) / 3 + 1);

    /// <summary>Trimestre cuya declaración toca presentar a fecha <paramref name="hoy"/> (el anterior al actual).</summary>
    public static Trimestre APresentar(DateOnly hoy) => De(hoy).Anterior;

    public string Periodo => $"{Numero}T";
    public string Etiqueta => $"{Numero}T {Ejercicio}";

    public DateOnly Inicio => new(Ejercicio, (Numero - 1) * 3 + 1, 1);
    /// <summary>Límite exclusivo (primer día del trimestre siguiente).</summary>
    public DateOnly Fin => Inicio.AddMonths(3);

    public bool Contiene(DateOnly fecha) => fecha >= Inicio && fecha < Fin;

    public Trimestre Anterior => Numero == 1 ? new(Ejercicio - 1, 4) : new(Ejercicio, Numero - 1);
    public Trimestre Siguiente => Numero == 4 ? new(Ejercicio + 1, 1) : new(Ejercicio, Numero + 1);

    /// <summary>Último día para presentar 303/130: día 20 del mes siguiente (30 de enero para el 4T).
    /// Si cae en fin de semana pasa al lunes. No contempla festivos.</summary>
    public DateOnly PlazoPresentacion
    {
        get
        {
            var dia = Numero == 4 ? new DateOnly(Ejercicio + 1, 1, 30) : new DateOnly(Ejercicio, Numero * 3 + 1, 20);
            while (dia.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) dia = dia.AddDays(1);
            return dia;
        }
    }

    public int CompareTo(Trimestre otro) => (Ejercicio, Numero).CompareTo((otro.Ejercicio, otro.Numero));
}

/// <summary>Filtro de periodo para listados: un ejercicio completo o uno de sus trimestres.</summary>
public readonly record struct Periodo(int Ejercicio, int? Trimestre = null)
{
    public bool Contiene(DateOnly fecha) =>
        Trimestre is { } t ? new Trimestre(Ejercicio, t).Contiene(fecha) : fecha.Year == Ejercicio;

    public string Etiqueta => Trimestre is { } t ? $"{t}T {Ejercicio}" : Ejercicio.ToString(CultureInfo.InvariantCulture);
}

// MARK: Datos de entrada (independientes de la persistencia)

public sealed record DatosIngreso(DateOnly Fecha, decimal Base, decimal TipoIVA, decimal CuotaIVA, decimal Retencion);

public sealed record DatosGasto(
    DateOnly Fecha,
    decimal Base,
    decimal CuotaIVA,
    /// <summary>% de la cuota que es deducible (100 general, 50 vehículo de uso mixto…).</summary>
    decimal PorcentajeDeducibleIVA = 100,
    /// <summary>Sin factura completa (con tu NIF) el IVA no es deducible.</summary>
    bool FacturaCompleta = true,
    bool DeducibleIRPF = true,
    bool BienInversion = false)
{
    public decimal IvaDeducible => FacturaCompleta ? Calculo.Porcentaje(PorcentajeDeducibleIVA, CuotaIVA) : 0;

    /// <summary>Base que se declara en el 303 junto a la cuota deducible.</summary>
    public decimal BaseDeducible => IvaDeducible > 0 ? Calculo.Porcentaje(PorcentajeDeducibleIVA, Base) : 0;

    /// <summary>Gasto computable en IRPF: base + IVA no deducible. Los bienes de inversión se amortizan.</summary>
    public decimal GastoIRPF => DeducibleIRPF && !BienInversion ? Base + CuotaIVA - IvaDeducible : 0;
}

// MARK: Modelo 303 (IVA trimestral, régimen general)

public sealed class Liquidacion303
{
    public sealed record Devengo(decimal Tipo, decimal Base, decimal Cuota)
    {
        /// <summary>Casillas del 303 (base, cuota) para el tipo.</summary>
        public (string Base, string Cuota)? Casillas => Tipo switch
        {
            4 => ("01", "03"),
            10 => ("04", "06"),
            21 => ("07", "09"),
            _ => null,
        };
    }

    public Trimestre Trimestre { get; }
    public IReadOnlyList<Devengo> Devengos { get; }
    public decimal BaseCorrientes { get; }   // 28
    public decimal CuotaCorrientes { get; }  // 29
    public decimal BaseInversion { get; }    // 30
    public decimal CuotaInversion { get; }   // 31

    public Liquidacion303(Trimestre trimestre, IEnumerable<DatosIngreso> ingresos, IEnumerable<DatosGasto> gastos)
    {
        Trimestre = trimestre;
        Devengos = ingresos
            .Where(i => trimestre.Contiene(i.Fecha) && i.TipoIVA > 0)
            .GroupBy(i => i.TipoIVA)
            .Select(g => new Devengo(g.Key, g.Sum(i => i.Base), g.Sum(i => i.CuotaIVA)))
            .OrderBy(d => d.Tipo)
            .ToList();

        var deducibles = gastos.Where(g => trimestre.Contiene(g.Fecha) && g.IvaDeducible > 0).ToList();
        var corrientes = deducibles.Where(g => !g.BienInversion).ToList();
        var inversion = deducibles.Where(g => g.BienInversion).ToList();
        BaseCorrientes = corrientes.Sum(g => g.BaseDeducible);
        CuotaCorrientes = corrientes.Sum(g => g.IvaDeducible);
        BaseInversion = inversion.Sum(g => g.BaseDeducible);
        CuotaInversion = inversion.Sum(g => g.IvaDeducible);
    }

    /// <summary>Casilla 27.</summary>
    public decimal TotalDevengado => Devengos.Sum(d => d.Cuota);
    /// <summary>Casilla 45.</summary>
    public decimal TotalDeducir => CuotaCorrientes + CuotaInversion;
    /// <summary>Casilla 46.</summary>
    public decimal Resultado => TotalDevengado - TotalDeducir;

    /// <summary>Resultado tras aplicar cuotas pendientes de compensar de periodos anteriores.</summary>
    public decimal ResultadoCompensando(decimal pendiente) => Resultado - pendiente;
}

// MARK: Modelo 130 (pago fraccionado IRPF, estimación directa)

public sealed class Liquidacion130
{
    public Trimestre Trimestre { get; }
    public decimal Ingresos { get; }        // 01 acumulado desde enero
    public decimal Gastos { get; }          // 02 acumulado desde enero
    public decimal PagosAnteriores { get; } // 05
    public decimal Retenciones { get; }     // 06 acumulado desde enero

    public Liquidacion130(Trimestre trimestre, IEnumerable<DatosIngreso> ingresos, IEnumerable<DatosGasto> gastos,
                          decimal pagosAnteriores)
    {
        Trimestre = trimestre;
        var desde = new Trimestre(trimestre.Ejercicio, 1).Inicio;
        bool Acumulado(DateOnly f) => f >= desde && f < trimestre.Fin;
        var delAño = ingresos.Where(i => Acumulado(i.Fecha)).ToList();
        Ingresos = delAño.Sum(i => i.Base);
        Retenciones = delAño.Sum(i => i.Retencion);
        Gastos = gastos.Where(g => Acumulado(g.Fecha)).Sum(g => g.GastoIRPF);
        PagosAnteriores = pagosAnteriores;
    }

    /// <summary>Casilla 03.</summary>
    public decimal Rendimiento => Ingresos - Gastos;
    /// <summary>Casilla 04: 20 % del rendimiento positivo.</summary>
    public decimal Cuota => Math.Max(0, Calculo.Porcentaje(20, Rendimiento));
    /// <summary>Casilla 07. Si es ≤ 0 la declaración sale negativa.</summary>
    public decimal Resultado => Cuota - PagosAnteriores - Retenciones;

    /// <summary>% de ingresos del ejercicio sujetos a retención. Si el año anterior fue ≥ 70 %,
    /// no hay obligación de presentar el 130 (art. 109.1 RIRPF).</summary>
    public static decimal? PorcentajeConRetencion(IEnumerable<DatosIngreso> ingresos, int ejercicio)
    {
        var delAño = ingresos.Where(i => i.Fecha.Year == ejercicio).ToList();
        var total = delAño.Sum(i => i.Base);
        if (total <= 0) return null;
        return Calculo.Redondear(delAño.Where(i => i.Retencion > 0).Sum(i => i.Base) * 100 / total, 1);
    }
}
