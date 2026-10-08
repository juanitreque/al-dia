using AlDia.Core;
using AlDia.Datos;
using AlDia.Servicios;
using Avalonia.Controls;

namespace AlDia.Vistas;

public sealed class ClientesVista : Seccion
{
    public override string Nombre => "Clientes";
    private readonly DataGrid tabla = Ui.Tabla(("Nombre", "Nombre", false), ("NIF", "Nif", false), ("Población", "Poblacion", false),
        ("Email", "Email", false), ("Teléfono", "Telefono", false));

    public ClientesVista()
    {
        tabla.DoubleTapped += async (_, _) => { if (tabla.SelectedItem is Cliente c) await Editar(c); };
        var raiz = new DockPanel();
        var cabecera = new StackPanel
        {
            Children =
            {
                Ui.Titulo("Clientes"),
                Ui.Barra(Ui.Boton("Nuevo cliente", () => Editar(new Cliente()), principal: true),
                         Ui.Boton("Editar", () => tabla.SelectedItem is Cliente c ? Editar(c) : Task.CompletedTask),
                         Ui.Boton("Eliminar", Eliminar),
                         Ui.Boton("Exportar para la app de la AEAT…", Exportar)),
            },
        };
        DockPanel.SetDock(cabecera, Dock.Top);
        raiz.Children.Add(cabecera);
        raiz.Children.Add(tabla);
        Content = raiz;
    }

    public override void Refrescar() => tabla.ItemsSource = Almacen.Clientes();

    private async Task Editar(Cliente c)
    {
        var nombre = Ui.Texto(c.Nombre, "Razón social o nombre");
        var nif = Ui.Texto(c.Nif, "B12345678", 160);
        var direccion = Ui.Texto(c.Direccion, "C/ Mayor, 1");
        var (cp, poblacion, provincia) = CamposLocalidad(c.CodigoPostal, c.Poblacion, c.Provincia);
        var email = Ui.Texto(c.Email, "facturas@cliente.es");
        var telefono = Ui.Texto(c.Telefono, "", 160);
        var notas = Ui.Texto(c.Notas);
        var formulario = Ui.Formulario(("Nombre", nombre), ("NIF", nif), ("Dirección", direccion), ("Código postal", cp),
            ("Población", poblacion), ("Provincia", provincia), ("Email", email), ("Teléfono", telefono), ("Notas", notas));
        if (!await Ui.Dialogo(Dueño, c.Id == 0 ? "Nuevo cliente" : c.Nombre, formulario,
                validar: () => string.IsNullOrWhiteSpace(nombre.Text) ? "Indica el nombre." : null))
            return;
        c.Nombre = nombre.Text!.Trim();
        c.Nif = (nif.Text ?? "").Trim().ToUpperInvariant();
        c.Direccion = (direccion.Text ?? "").Trim();
        c.CodigoPostal = (cp.Text ?? "").Trim();
        c.Poblacion = (poblacion.Text ?? "").Trim();
        c.Provincia = (provincia.Text ?? "").Trim();
        c.Email = (email.Text ?? "").Trim();
        c.Telefono = (telefono.Text ?? "").Trim();
        c.Notas = (notas.Text ?? "").Trim();
        Almacen.Guardar(c);
        Refrescar();
    }

    /// <summary>Código postal que rellena la provincia (y la población si es la capital).</summary>
    public static (TextBox Cp, TextBox Poblacion, TextBox Provincia) CamposLocalidad(string cp, string poblacion, string provincia)
    {
        var tCp = Ui.Texto(cp, "28001", 100);
        var tPob = Ui.Texto(poblacion);
        var tProv = Ui.Texto(provincia);
        tCp.TextChanged += (_, _) =>
        {
            if (CodigoPostal.Provincia(tCp.Text ?? "") is not { } p) return;
            tProv.Text = p;
            // Los códigos acabados en 0xx son de la capital de provincia
            if (string.IsNullOrWhiteSpace(tPob.Text) && tCp.Text!.Trim()[2] == '0')
                tPob.Text = p.Split('/')[0];
        };
        return (tCp, tPob, tProv);
    }

    private async Task Eliminar()
    {
        if (tabla.SelectedItem is not Cliente c) return;
        if (await Ui.Confirmar(Dueño, "Eliminar cliente", $"¿Eliminar a {c.Nombre}? Sus facturas se conservan, sin cliente asignado."))
        {
            Almacen.Borrar(c);
            Refrescar();
        }
    }

    private async Task Exportar()
    {
        if (await Ui.GuardarComo(Dueño, "Clientes para la app de la AEAT", "Clientes.txt", "txt") is { } ruta)
        {
            await File.WriteAllTextAsync(ruta, ExportacionAEAT.Clientes(Almacen.Clientes().Select(c => c.ParaAEAT)));
            await Ui.Mensaje(Dueño, "Exportado", "En la app de la AEAT: Otros servicios → Clientes → Importar, y elige este archivo.");
        }
    }
}

public sealed class ServiciosVista : Seccion
{
    public override string Nombre => "Servicios";
    private readonly DataGrid tabla = Ui.Tabla(("Código", "Codigo", false), ("Descripción", "Descripcion", false),
        ("Precio", "PrecioTexto", true), ("IVA", "IvaTexto", true));

    private sealed record Fila(Servicio S)
    {
        public string Codigo => S.Codigo;
        public string Descripcion => S.Descripcion;
        public string PrecioTexto => Formato.Euros(S.Precio);
        public string IvaTexto => Formato.Porcentaje(S.TipoIVA);
    }

    public ServiciosVista()
    {
        tabla.DoubleTapped += async (_, _) => { if (tabla.SelectedItem is Fila f) await Editar(f.S); };
        var raiz = new DockPanel();
        var cabecera = new StackPanel
        {
            Children =
            {
                Ui.Titulo("Servicios"),
                Ui.Nota("Catálogo de lo que facturas habitualmente: se añade a las facturas con un clic."),
                Ui.Barra(Ui.Boton("Nuevo servicio", () => Editar(new Servicio { TipoIVA = Ajustes.IvaPorDefecto }), principal: true),
                         Ui.Boton("Editar", () => tabla.SelectedItem is Fila f ? Editar(f.S) : Task.CompletedTask),
                         Ui.Boton("Eliminar", Eliminar),
                         Ui.Boton("Exportar para la app de la AEAT…", Exportar)),
            },
        };
        DockPanel.SetDock(cabecera, Dock.Top);
        raiz.Children.Add(cabecera);
        raiz.Children.Add(tabla);
        Content = raiz;
    }

    public override void Refrescar() => tabla.ItemsSource = Almacen.Servicios().Select(s => new Fila(s)).ToList();

    private async Task Editar(Servicio s)
    {
        var codigo = Ui.Texto(s.Codigo, "CONSULTORIA", 200);
        var descripcion = Ui.Texto(s.Descripcion, "Hora de consultoría");
        var precio = Ui.Importe(s.Precio);
        var iva = Ui.Opciones(new decimal[] { 0, 4, 10, 21 }.Union([s.TipoIVA]).Order().Select(Formato.Porcentaje).ToList(), Formato.Porcentaje(s.TipoIVA), 110);
        if (!await Ui.Dialogo(Dueño, s.Id == 0 ? "Nuevo servicio" : s.Descripcion,
                Ui.Formulario(("Código", codigo), ("Descripción", descripcion), ("Precio (sin IVA)", precio), ("IVA", iva)),
                validar: () => string.IsNullOrWhiteSpace(descripcion.Text) ? "Indica la descripción." : null))
            return;
        s.Codigo = (codigo.Text ?? "").Trim();
        s.Descripcion = descripcion.Text!.Trim();
        s.Precio = Ui.Leer(precio);
        s.TipoIVA = decimal.Parse(((string)iva.SelectedItem!).Replace(" %", ""), Formato.Es);
        Almacen.Guardar(s);
        Refrescar();
    }

    private async Task Eliminar()
    {
        if (tabla.SelectedItem is not Fila f) return;
        if (await Ui.Confirmar(Dueño, "Eliminar servicio", $"¿Eliminar «{f.S.Descripcion}»?"))
        {
            Almacen.Borrar(f.S);
            Refrescar();
        }
    }

    private async Task Exportar()
    {
        if (await Ui.GuardarComo(Dueño, "Productos para la app de la AEAT", "Productos.txt", "txt") is { } ruta)
        {
            await File.WriteAllTextAsync(ruta, ExportacionAEAT.Productos(Almacen.Servicios().Select(s => s.ParaAEAT)));
            await Ui.Mensaje(Dueño, "Exportado", "En la app de la AEAT: Otros servicios → Productos → Importar, y elige este archivo.");
        }
    }
}
