using System.Data;
using System.Security.Cryptography;
using System.Text.Json;

namespace InventarioSanMarcos;

internal sealed class Settings
{
    public string Mode { get; set; } = "Demo";
    public string ConnectionString { get; set; } = @"Server=(localdb)\MSSQLLocalDB;Database=InventarioSanMarcos;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;Connect Timeout=8";
    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "InventarioSanMarcos");
    public static string FilePath => Path.Combine(Folder, "settings.json");
    public static Settings Load() => File.Exists(FilePath) ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new() : new();
    public void Save() { Directory.CreateDirectory(Folder); File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true })); }
}

internal static class Passwords
{
    public static string Hash(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 210000, HashAlgorithmName.SHA256, 32);
        return "210000." + Convert.ToBase64String(salt) + "." + Convert.ToBase64String(hash);
    }
    public static bool Verify(string password, string encoded)
    {
        try
        {
            var p = encoded.Split('.');
            int count = int.Parse(p[0]);
            if (count < 100000 || count > 2000000) return false;
            return CryptographicOperations.FixedTimeEquals(Convert.FromBase64String(p[2]), Rfc2898DeriveBytes.Pbkdf2(password, Convert.FromBase64String(p[1]), count, HashAlgorithmName.SHA256, 32));
        }
        catch (Exception e) when (e is FormatException or IndexOutOfRangeException or OverflowException) { return false; }
    }
}

internal record Field(string Name, string Label, string Kind = "text", string[]? Choices = null, string? Lookup = null, bool Required = true);
internal record Module(string Table, string Title, string Description, Field[] Fields);

internal static class Catalog
{
    public static readonly string[] Types = ["Computadora", "Laptop", "Monitor", "Teclado", "Mouse", "Impresora", "Proyector", "Otro"];
    public static readonly string[] States = ["Operativo", "Mantenimiento", "De baja"];
    public static readonly Module[] Modules = [
        new("Equipos", "Equipos", "Computadoras y periféricos registrados", [
            new("Codigo", "Código"), new("Tipo", "Tipo", "choice", Types), new("Marca", "Marca"), new("Modelo", "Modelo"),
            new("Serie", "N.º de serie", Required:false), new("Procesador", "Procesador", Required:false),
            new("Ram", "Memoria RAM", Required:false), new("Almacenamiento", "Almacenamiento", Required:false),
            new("UbicacionId", "Ubicación", "lookup", Lookup:"Ubicaciones"), new("ResponsableId", "Responsable", "lookup", Lookup:"Responsables", Required:false),
            new("Estado", "Estado", "choice", States), new("Observaciones", "Observaciones", "notes", Required:false)]),
        new("Ubicaciones", "Ubicaciones", "Laboratorios, aulas y oficinas", [new("Nombre", "Nombre"), new("Descripcion", "Descripción", "notes", Required:false)]),
        new("Responsables", "Responsables", "Personal a cargo de los equipos", [new("Nombre", "Nombre completo"), new("Documento", "Documento", Required:false), new("Cargo", "Cargo", Required:false), new("Contacto", "Contacto", Required:false)]),
        new("Mantenimientos", "Mantenimiento", "Historial de fallas y reparaciones", [
            new("EquipoId", "Equipo", "lookup", Lookup:"Equipos"), new("Fecha", "Fecha", "date"), new("Tipo", "Tipo", "choice", ["Preventivo", "Correctivo"]),
            new("Tecnico", "Técnico"), new("Detalle", "Falla / trabajo realizado", "notes"), new("Costo", "Costo (S/)", "money"),
            new("Estado", "Estado del servicio", "choice", ["Pendiente", "En proceso", "Finalizado"])]),
        new("Usuarios", "Usuarios", "Acceso y permisos de la aplicación", [new("Nombre", "Nombre completo"), new("Login", "Usuario"), new("Rol", "Rol", "choice", ["Administrador", "Encargado"]), new("Activo", "Activo", "bool")])
    ];
    public static DataSet Schema()
    {
        DataSet ds = new("Inventario") { CaseSensitive = false, Locale = System.Globalization.CultureInfo.GetCultureInfo("es-PE") };
        foreach (var m in Modules)
        {
            var t = ds.Tables.Add(m.Table);
            var id = t.Columns.Add("Id", typeof(Guid)); t.PrimaryKey = [id];
            foreach (var f in m.Fields)
            {
                Type type = f.Kind switch { "lookup" => typeof(Guid), "date" => typeof(DateTime), "money" => typeof(decimal), "bool" => typeof(bool), _ => typeof(string) };
                var c = t.Columns.Add(f.Name, type); c.AllowDBNull = !f.Required;
                if (type == typeof(string)) c.MaxLength = f.Kind == "notes" ? 1000 : 150;
            }
            if (m.Table == "Usuarios") { var c = t.Columns.Add("PasswordHash", typeof(string)); c.MaxLength = 300; c.AllowDBNull = false; }
        }
        ds.Tables["Equipos"]!.Constraints.Add(new UniqueConstraint("UQ_Equipo_Codigo", ds.Tables["Equipos"]!.Columns["Codigo"]!));
        ds.Tables["Ubicaciones"]!.Constraints.Add(new UniqueConstraint("UQ_Ubicacion", ds.Tables["Ubicaciones"]!.Columns["Nombre"]!));
        ds.Tables["Usuarios"]!.Constraints.Add(new UniqueConstraint("UQ_Login", ds.Tables["Usuarios"]!.Columns["Login"]!));
        Relate(ds, "Ubicaciones", "Equipos", "UbicacionId");
        Relate(ds, "Responsables", "Equipos", "ResponsableId");
        Relate(ds, "Equipos", "Mantenimientos", "EquipoId");
        return ds;
    }
    private static void Relate(DataSet ds, string parent, string child, string column)
    {
        var rel = ds.Relations.Add(parent + child, ds.Tables[parent]!.Columns["Id"]!, ds.Tables[child]!.Columns[column]!);
        rel.ChildKeyConstraint!.DeleteRule = Rule.None;
        rel.ChildKeyConstraint.UpdateRule = Rule.None;
    }
}
