using AlDia.Core;

namespace AlDia.Datos;

/// <summary>Con el argumento --demo la app arranca con una base temporal llena de datos ficticios.</summary>
public static class Demo
{
    public static bool Activo { get; set; }
    public static string Carpeta => Path.Combine(Path.GetTempPath(), "AlDiaDemo");

    public static void Rellenar()
    {
        static DateOnly Dia(int mes, int d) => new(2026, mes, Math.Min(d, DateTime.DaysInMonth(2026, mes)));
        static DateOnly FinDeMes(int mes) => new DateOnly(2026, mes, 1).AddMonths(1).AddDays(-1);

        Almacen.Ajuste("emisor.nombre", "Tu Nombre Apellido");
        Almacen.Ajuste("emisor.nif", "00000000T");
        Almacen.Ajuste("emisor.direccion", "C/ Mayor, 1");
        Almacen.Ajuste("emisor.cp", "28001");
        Almacen.Ajuste("emisor.poblacion", "Madrid");
        Almacen.Ajuste("emisor.provincia", "Madrid");
        Almacen.Ajuste("emisor.email", "tu@correo.es");
        Almacen.Ajuste("emisor.iban", "ES9121000418450200051332");
        Almacen.Ajuste("factura.pie", PieFactura.ClausulaRGPD);

        Cliente NuevoCliente(string nombre, string nif, string cp, string poblacion)
        {
            var c = new Cliente
            {
                Nombre = nombre, Nif = nif, Direccion = "C/ Ejemplo, 10", CodigoPostal = cp, Poblacion = poblacion,
                Provincia = CodigoPostal.Provincia(cp) ?? "", Email = "facturas@ejemplo.es",
            };
            Almacen.Guardar(c);
            return c;
        }
        var estudio = NuevoCliente("Estudio Norte, SL", "B00000001", "28001", "Madrid");
        var cafeteria = NuevoCliente("Cafetería La Plaza, SL", "B00000002", "46001", "Valencia");
        var asesoria = NuevoCliente("Asesoría Lumen, SL", "B00000003", "41001", "Sevilla");
        var talleres = NuevoCliente("Talleres Ruiz, SL", "B00000004", "48001", "Bilbao");

        Servicio NuevoServicio(string codigo, string descripcion, decimal precio)
        {
            var s = new Servicio { Codigo = codigo, Descripcion = descripcion, Precio = precio };
            Almacen.Guardar(s);
            return s;
        }
        var consultoria = NuevoServicio("CONSULTORIA", "Hora de consultoría", 40);
        var web = NuevoServicio("WEB", "Diseño de página web", 650);
        var mantenimiento = NuevoServicio("MANTENIMIENTO", "Mantenimiento web mensual", 90);

        var contador = 0;
        void Factura(DateOnly fecha, Cliente cliente, Servicio s, decimal cantidad, bool borrador = false, bool cobrada = true)
        {
            contador++;
            var i = new Ingreso
            {
                Borrador = borrador, Numero = $"2026-{contador:000}", Fecha = fecha, Cliente = cliente,
                Concepto = s.Descripcion, Cobrada = !borrador && cobrada,
                Lineas = [new LineaIngreso { Codigo = s.Codigo, Concepto = s.Descripcion, Cantidad = cantidad, Precio = s.Precio }],
            };
            i.FechaCobro = i.Cobrada ? fecha.AddDays(15) : null;
            i.Recalcular();
            Almacen.Guardar(i);
        }

        decimal[] horas = [12, 9, 15, 10, 14, 8, 6, 11, 13];
        for (var mes = 1; mes <= 9; mes++)
        {
            var pagada = mes < 9;
            Factura(Dia(mes, 10), estudio, consultoria, horas[mes - 1], cobrada: pagada);
            foreach (var c in new[] { cafeteria, asesoria, talleres }) Factura(FinDeMes(mes), c, mantenimiento, 1, cobrada: pagada);
            if (mes == 3) Factura(Dia(mes, 20), cafeteria, web, 1);
            if (mes == 6) Factura(Dia(mes, 20), talleres, web, 1);
        }
        Factura(Dia(10, 10), estudio, consultoria, 12, borrador: true);
        Factura(FinDeMes(10), cafeteria, mantenimiento, 1, borrador: true);

        void NuevoGasto(DateOnly fecha, string proveedor, string concepto, string categoria, decimal @base, decimal iva = 21) =>
            Almacen.Guardar(new Gasto
            {
                Fecha = fecha, Proveedor = proveedor, Concepto = concepto, Categoria = categoria, Base = @base, TipoIVA = iva,
                CuotaIVA = Calculo.Porcentaje(iva, @base), NumeroFactura = $"F-{fecha.Month:00}{fecha.Day:00}",
            });
        for (var mes = 1; mes <= 9; mes++)
        {
            NuevoGasto(Dia(mes, 1), "Proveedor de software", "Suscripción de software", "Software y suscripciones", 24.19m);
            NuevoGasto(Dia(mes, 5), "Espacio de coworking", "Puesto fijo mensual", "Otros", 150);
            NuevoGasto(Dia(mes, 28), "Operador móvil", "Línea móvil", "Teléfono e internet", 24.79m);
            NuevoGasto(Dia(mes, 30), "Seguridad Social", "Cuota de autónomos", "Cuota de autónomos (RETA)", 294, iva: 0);
        }
        foreach (var mes in new[] { 3, 6, 9 })
            NuevoGasto(Dia(mes, 25), "Gestoría Ejemplo", "Asesoría fiscal trimestral", "Gestoría y asesoría", 60);
        NuevoGasto(Dia(4, 20), "Escuela online", "Curso de formación", "Formación y certificaciones", 120);
        NuevoGasto(Dia(9, 21), "Papelería Ejemplo", "Tóner y papel", "Material y equipamiento", 49.59m);

        var ingresos = Almacen.Ingresos().Where(i => !i.Borrador).Select(i => i.Datos).ToList();
        var gastos = Almacen.Gastos().Select(g => g.Datos).ToList();
        for (var n = 1; n <= 2; n++)
        {
            var t = new Trimestre(2026, n);
            Almacen.Guardar(new ModeloPresentado
            {
                Modelo = "303", Ejercicio = 2026, Periodo = t.Periodo,
                Importe = new Liquidacion303(t, ingresos, gastos).Resultado,
                FechaPresentacion = t.PlazoPresentacion.AddDays(-3), Justificante = $"NRC 0000000000{n}",
            });
        }
    }
}
