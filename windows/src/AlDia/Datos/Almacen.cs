using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace AlDia.Datos;

/// <summary>Base de datos SQLite en %APPDATA%\AlDia\AlDia.db (adjuntos incluidos).</summary>
public static class Almacen
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Carpeta { get; private set; } = "";
    private static string cadena = "";

    public static void Abrir(string carpeta)
    {
        Carpeta = carpeta;
        Directory.CreateDirectory(carpeta);
        cadena = new SqliteConnectionStringBuilder { DataSource = Path.Combine(carpeta, "AlDia.db") }.ToString();
        Ejecutar("""
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS ajustes (clave TEXT PRIMARY KEY, valor TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS clientes (
                id INTEGER PRIMARY KEY, nombre TEXT, nif TEXT, direccion TEXT, cp TEXT, poblacion TEXT,
                provincia TEXT, pais TEXT, email TEXT, telefono TEXT, notas TEXT);
            CREATE TABLE IF NOT EXISTS servicios (
                id INTEGER PRIMARY KEY, codigo TEXT, descripcion TEXT, precio TEXT, tipo_iva TEXT);
            CREATE TABLE IF NOT EXISTS ingresos (
                id INTEGER PRIMARY KEY, borrador INTEGER, numero TEXT, fecha TEXT, cliente_id INTEGER, concepto TEXT,
                base TEXT, tipo_iva TEXT, cuota_iva TEXT, tipo_retencion TEXT, retencion TEXT, cobrada INTEGER,
                fecha_cobro TEXT, notas TEXT, lineas TEXT, adjunto BLOB, adjunto_nombre TEXT);
            CREATE TABLE IF NOT EXISTS gastos (
                id INTEGER PRIMARY KEY, fecha TEXT, proveedor TEXT, nif TEXT, numero TEXT, concepto TEXT, categoria TEXT,
                base TEXT, tipo_iva TEXT, cuota_iva TEXT, deducible TEXT, completa INTEGER, irpf INTEGER, inversion INTEGER,
                notas TEXT, adjunto BLOB, adjunto_nombre TEXT);
            CREATE TABLE IF NOT EXISTS modelos (
                id INTEGER PRIMARY KEY, modelo TEXT, ejercicio INTEGER, periodo TEXT, importe TEXT, resultado TEXT,
                fecha TEXT, justificante TEXT, notas TEXT, adjunto BLOB, adjunto_nombre TEXT);
            """);
    }

    // MARK: Ajustes

    public static string Ajuste(string clave) =>
        Consultar("SELECT valor FROM ajustes WHERE clave = $c", r => r.GetString(0), ("$c", clave)).FirstOrDefault() ?? "";

    public static void Ajuste(string clave, string valor) =>
        Ejecutar("INSERT INTO ajustes (clave, valor) VALUES ($c, $v) ON CONFLICT(clave) DO UPDATE SET valor = $v",
                 ("$c", clave), ("$v", valor.Trim()));

    // MARK: Clientes

    public static List<Cliente> Clientes() => Consultar("SELECT * FROM clientes ORDER BY nombre COLLATE NOCASE", r => new Cliente
    {
        Id = r.GetInt64(0), Nombre = Txt(r, 1), Nif = Txt(r, 2), Direccion = Txt(r, 3), CodigoPostal = Txt(r, 4),
        Poblacion = Txt(r, 5), Provincia = Txt(r, 6), Pais = Txt(r, 7), Email = Txt(r, 8), Telefono = Txt(r, 9), Notas = Txt(r, 10),
    });

    public static void Guardar(Cliente c) => c.Id = Upsert("clientes", c.Id,
        ("nombre", c.Nombre), ("nif", c.Nif), ("direccion", c.Direccion), ("cp", c.CodigoPostal), ("poblacion", c.Poblacion),
        ("provincia", c.Provincia), ("pais", c.Pais), ("email", c.Email), ("telefono", c.Telefono), ("notas", c.Notas));

    public static void Borrar(Cliente c)
    {
        Ejecutar("UPDATE ingresos SET cliente_id = NULL WHERE cliente_id = $id", ("$id", c.Id));
        Ejecutar("DELETE FROM clientes WHERE id = $id", ("$id", c.Id));
    }

    // MARK: Servicios

    public static List<Servicio> Servicios() => Consultar("SELECT * FROM servicios ORDER BY codigo COLLATE NOCASE", r => new Servicio
    {
        Id = r.GetInt64(0), Codigo = Txt(r, 1), Descripcion = Txt(r, 2), Precio = Dec(r, 3), TipoIVA = Dec(r, 4),
    });

    public static void Guardar(Servicio s) => s.Id = Upsert("servicios", s.Id,
        ("codigo", s.Codigo), ("descripcion", s.Descripcion), ("precio", D(s.Precio)), ("tipo_iva", D(s.TipoIVA)));

    public static void Borrar(Servicio s) => Ejecutar("DELETE FROM servicios WHERE id = $id", ("$id", s.Id));

    // MARK: Ingresos

    /// <summary>Todas las facturas, con su cliente, sin los adjuntos (se cargan aparte con <see cref="AdjuntoIngreso"/>).</summary>
    public static List<Ingreso> Ingresos()
    {
        var clientes = Clientes().ToDictionary(c => c.Id);
        return Consultar("""
            SELECT id, borrador, numero, fecha, cliente_id, concepto, base, tipo_iva, cuota_iva, tipo_retencion, retencion,
                   cobrada, fecha_cobro, notas, lineas, adjunto_nombre, length(adjunto)
            FROM ingresos ORDER BY fecha, numero
            """, r =>
        {
            var i = new Ingreso
            {
                Id = r.GetInt64(0), Borrador = r.GetInt64(1) != 0, Numero = Txt(r, 2), Fecha = Fecha(r, 3)!.Value,
                ClienteId = r.IsDBNull(4) ? null : r.GetInt64(4), Concepto = Txt(r, 5), Base = Dec(r, 6), TipoIVA = Dec(r, 7),
                CuotaIVA = Dec(r, 8), TipoRetencion = Dec(r, 9), Retencion = Dec(r, 10), Cobrada = r.GetInt64(11) != 0,
                FechaCobro = Fecha(r, 12), Notas = Txt(r, 13),
                Lineas = JsonSerializer.Deserialize<List<LineaIngreso>>(r.IsDBNull(14) ? "[]" : r.GetString(14)) ?? [],
                AdjuntoNombre = r.IsDBNull(15) ? null : r.GetString(15),
                TieneDocumento = !r.IsDBNull(16) && r.GetInt64(16) > 0,
            };
            if (i.ClienteId is { } id) i.Cliente = clientes.GetValueOrDefault(id);
            return i;
        });
    }

    public static byte[]? AdjuntoIngreso(long id) =>
        Consultar("SELECT adjunto FROM ingresos WHERE id = $id", r => r.IsDBNull(0) ? null : (byte[])r[0], ("$id", id)).FirstOrDefault();

    /// <summary>Guarda la factura. El adjunto solo se escribe si <paramref name="conAdjunto"/>.</summary>
    public static void Guardar(Ingreso i, bool conAdjunto = false)
    {
        var campos = new List<(string, object?)>
        {
            ("borrador", i.Borrador ? 1 : 0), ("numero", i.Numero), ("fecha", F(i.Fecha)), ("cliente_id", i.Cliente?.Id ?? i.ClienteId),
            ("concepto", i.Concepto), ("base", D(i.Base)), ("tipo_iva", D(i.TipoIVA)), ("cuota_iva", D(i.CuotaIVA)),
            ("tipo_retencion", D(i.TipoRetencion)), ("retencion", D(i.Retencion)), ("cobrada", i.Cobrada ? 1 : 0),
            ("fecha_cobro", i.FechaCobro is { } fc ? F(fc) : null), ("notas", i.Notas),
            ("lineas", JsonSerializer.Serialize(i.Lineas)),
        };
        if (conAdjunto) { campos.Add(("adjunto", i.Adjunto)); campos.Add(("adjunto_nombre", i.AdjuntoNombre)); }
        i.Id = Upsert("ingresos", i.Id, [.. campos]);
    }

    public static void Borrar(Ingreso i) => Ejecutar("DELETE FROM ingresos WHERE id = $id", ("$id", i.Id));

    // MARK: Gastos

    public static List<Gasto> Gastos() => Consultar("""
        SELECT id, fecha, proveedor, nif, numero, concepto, categoria, base, tipo_iva, cuota_iva, deducible, completa, irpf,
               inversion, notas, adjunto_nombre, length(adjunto)
        FROM gastos ORDER BY fecha
        """, r => new Gasto
    {
        Id = r.GetInt64(0), Fecha = Fecha(r, 1)!.Value, Proveedor = Txt(r, 2), NifProveedor = Txt(r, 3), NumeroFactura = Txt(r, 4),
        Concepto = Txt(r, 5), Categoria = Txt(r, 6), Base = Dec(r, 7), TipoIVA = Dec(r, 8), CuotaIVA = Dec(r, 9),
        PorcentajeDeducibleIVA = Dec(r, 10), FacturaCompleta = r.GetInt64(11) != 0, DeducibleIRPF = r.GetInt64(12) != 0,
        BienInversion = r.GetInt64(13) != 0, Notas = Txt(r, 14), AdjuntoNombre = r.IsDBNull(15) ? null : r.GetString(15),
        TieneDocumento = !r.IsDBNull(16) && r.GetInt64(16) > 0,
    });

    public static byte[]? AdjuntoGasto(long id) =>
        Consultar("SELECT adjunto FROM gastos WHERE id = $id", r => r.IsDBNull(0) ? null : (byte[])r[0], ("$id", id)).FirstOrDefault();

    public static void Guardar(Gasto g, bool conAdjunto = false)
    {
        var campos = new List<(string, object?)>
        {
            ("fecha", F(g.Fecha)), ("proveedor", g.Proveedor), ("nif", g.NifProveedor), ("numero", g.NumeroFactura),
            ("concepto", g.Concepto), ("categoria", g.Categoria), ("base", D(g.Base)), ("tipo_iva", D(g.TipoIVA)),
            ("cuota_iva", D(g.CuotaIVA)), ("deducible", D(g.PorcentajeDeducibleIVA)), ("completa", g.FacturaCompleta ? 1 : 0),
            ("irpf", g.DeducibleIRPF ? 1 : 0), ("inversion", g.BienInversion ? 1 : 0), ("notas", g.Notas),
        };
        if (conAdjunto) { campos.Add(("adjunto", g.Adjunto)); campos.Add(("adjunto_nombre", g.AdjuntoNombre)); }
        g.Id = Upsert("gastos", g.Id, [.. campos]);
    }

    public static void Borrar(Gasto g) => Ejecutar("DELETE FROM gastos WHERE id = $id", ("$id", g.Id));

    // MARK: Modelos presentados

    public static List<ModeloPresentado> Modelos() => Consultar("""
        SELECT id, modelo, ejercicio, periodo, importe, resultado, fecha, justificante, notas, adjunto_nombre, length(adjunto)
        FROM modelos ORDER BY ejercicio DESC, periodo DESC, modelo
        """, r => new ModeloPresentado
    {
        Id = r.GetInt64(0), Modelo = Txt(r, 1), Ejercicio = (int)r.GetInt64(2), Periodo = Txt(r, 3), Importe = Dec(r, 4),
        TipoResultado = Txt(r, 5), FechaPresentacion = Fecha(r, 6)!.Value, Justificante = Txt(r, 7), Notas = Txt(r, 8),
        AdjuntoNombre = r.IsDBNull(9) ? null : r.GetString(9), TieneDocumento = !r.IsDBNull(10) && r.GetInt64(10) > 0,
    });

    public static byte[]? AdjuntoModelo(long id) =>
        Consultar("SELECT adjunto FROM modelos WHERE id = $id", r => r.IsDBNull(0) ? null : (byte[])r[0], ("$id", id)).FirstOrDefault();

    public static void Guardar(ModeloPresentado m, bool conAdjunto = false)
    {
        var campos = new List<(string, object?)>
        {
            ("modelo", m.Modelo), ("ejercicio", m.Ejercicio), ("periodo", m.Periodo), ("importe", D(m.Importe)),
            ("resultado", m.TipoResultado), ("fecha", F(m.FechaPresentacion)), ("justificante", m.Justificante), ("notas", m.Notas),
        };
        if (conAdjunto) { campos.Add(("adjunto", m.Adjunto)); campos.Add(("adjunto_nombre", m.AdjuntoNombre)); }
        m.Id = Upsert("modelos", m.Id, [.. campos]);
    }

    public static void Borrar(ModeloPresentado m) => Ejecutar("DELETE FROM modelos WHERE id = $id", ("$id", m.Id));

    // MARK: SQL

    private static SqliteConnection Conexion()
    {
        var c = new SqliteConnection(cadena);
        c.Open();
        return c;
    }

    private static void Ejecutar(string sql, params (string, object?)[] parametros)
    {
        using var c = Conexion();
        using var cmd = Comando(c, sql, parametros);
        cmd.ExecuteNonQuery();
    }

    private static List<T> Consultar<T>(string sql, Func<SqliteDataReader, T> leer, params (string, object?)[] parametros)
    {
        using var c = Conexion();
        using var cmd = Comando(c, sql, parametros);
        using var r = cmd.ExecuteReader();
        var lista = new List<T>();
        while (r.Read()) lista.Add(leer(r));
        return lista;
    }

    private static SqliteCommand Comando(SqliteConnection c, string sql, (string, object?)[] parametros)
    {
        var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (nombre, valor) in parametros) cmd.Parameters.AddWithValue(nombre, valor ?? DBNull.Value);
        return cmd;
    }

    /// <summary>Inserta (id 0) o actualiza la fila; devuelve el id.</summary>
    private static long Upsert(string tabla, long id, params (string Campo, object? Valor)[] campos)
    {
        var parametros = campos.Select((c, n) => ($"$p{n}", c.Valor)).ToList();
        using var c = Conexion();
        if (id == 0)
        {
            var sql = $"INSERT INTO {tabla} ({string.Join(", ", campos.Select(x => x.Campo))}) " +
                      $"VALUES ({string.Join(", ", parametros.Select(p => p.Item1))}); SELECT last_insert_rowid();";
            using var cmd = Comando(c, sql, [.. parametros]);
            return (long)cmd.ExecuteScalar()!;
        }
        else
        {
            var sql = $"UPDATE {tabla} SET {string.Join(", ", campos.Select((x, n) => $"{x.Campo} = $p{n}"))} WHERE id = $id";
            using var cmd = Comando(c, sql, [.. parametros, ("$id", id)]);
            cmd.ExecuteNonQuery();
            return id;
        }
    }

    private static string Txt(SqliteDataReader r, int i) => r.IsDBNull(i) ? "" : r.GetString(i);
    private static decimal Dec(SqliteDataReader r, int i) => r.IsDBNull(i) ? 0 : decimal.Parse(r.GetString(i), Inv);
    private static DateOnly? Fecha(SqliteDataReader r, int i) =>
        r.IsDBNull(i) || r.GetString(i).Length == 0 ? null : DateOnly.ParseExact(r.GetString(i), "yyyy-MM-dd", Inv);
    private static string D(decimal v) => v.ToString(Inv);
    private static string F(DateOnly f) => f.ToString("yyyy-MM-dd", Inv);
}
