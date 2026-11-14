using System.Data;
using Microsoft.Data.SqlClient;

namespace InventarioSanMarcos;

internal sealed class Store
{
    private readonly Settings settings;
    private readonly string demoDirectory;
    public DataSet Data { get; private set; } = Catalog.Schema();
    public bool IsDemo => settings.Mode == "Demo";
    public string DemoPath => Path.Combine(demoDirectory, "demo.xml");
    public Store(Settings settings, string? demoDirectory = null) { this.settings = settings; this.demoDirectory = demoDirectory ?? Settings.Folder; Reload(); }
    public DataTable Table(string name) => Data.Tables[name]!;
    public void Reload()
    {
        var next = Catalog.Schema();
        next.EnforceConstraints = false;
        if (IsDemo)
        {
            if (File.Exists(DemoPath)) next.ReadXml(DemoPath, XmlReadMode.IgnoreSchema);
            else Seed(next);
        }
        else
        {
            using var connection = new SqlConnection(settings.ConnectionString);
            connection.Open();
            // One transaction keeps related tables consistent while other users are saving.
            using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
            foreach (DataTable table in next.Tables)
            {
                using var adapter = new SqlDataAdapter(new SqlCommand("SELECT * FROM dbo.[" + table.TableName + "]", connection, transaction));
                adapter.Fill(table);
            }
            transaction.Commit();
        }
        next.EnforceConstraints = true;
        next.AcceptChanges(); Data = next;
    }
    public void Save(string tableName, DataRow? existing, Dictionary<string, object> values)
    {
        var table = Table(tableName);
        var row = existing ?? table.NewRow();
        var merged = table.Columns.Cast<DataColumn>().ToDictionary(c => c.ColumnName, c => existing == null ? (object)DBNull.Value : existing[c]);
        foreach (var pair in values) merged[pair.Key] = pair.Value;
        Validate(tableName, existing, merged);
        try
        {
            row.BeginEdit();
            if (existing == null) row["Id"] = Guid.NewGuid();
            foreach (var pair in values) row[pair.Key] = pair.Value;
            row.EndEdit();
            if (existing == null) table.Rows.Add(row);
            Persist();
        }
        catch { row.CancelEdit(); Data.RejectChanges(); throw; }
    }
    public void Delete(string tableName, DataRow row)
    {
        if (tableName == "Usuarios" && (bool)row["Activo"] && (string)row["Rol"] == "Administrador" && ActiveAdmins() <= 1)
            throw new InvalidOperationException("Debe quedar al menos un administrador activo.");
        foreach (DataRelation relation in row.Table.ChildRelations)
            if (row.GetChildRows(relation).Length > 0) throw new InvalidOperationException("Este registro está en uso. Reasigna o elimina primero los registros relacionados.");
        try { row.Delete(); Persist(); } catch { Data.RejectChanges(); throw; }
    }
    private int ActiveAdmins() => Table("Usuarios").AsEnumerable().Count(r => r.RowState != DataRowState.Deleted && r.Field<bool>("Activo") && r.Field<string>("Rol") == "Administrador");
    private void Validate(string table, DataRow? existing, Dictionary<string, object> values)
    {
        var module = Catalog.Modules.First(m => m.Table == table);
        foreach (var f in module.Fields)
        {
            object v = values[f.Name];
            if (f.Required && (v == DBNull.Value || (v is string s && string.IsNullOrWhiteSpace(s)))) throw new InvalidOperationException("Completa el campo «" + f.Label + "».");
            if (f.Choices != null && !f.Choices.Contains(Convert.ToString(v))) throw new InvalidOperationException("Selecciona una opción válida para «" + f.Label + "».");
        }
        if (table == "Equipos" && values["Serie"] is string serial && serial.Length > 0 && Table(table).AsEnumerable().Any(r => r != existing && string.Equals(r.Field<string>("Serie"), serial, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Ya existe un equipo con ese número de serie.");
        if (table == "Mantenimientos" && (decimal)values["Costo"] < 0) throw new InvalidOperationException("El costo no puede ser negativo.");
        if (table == "Usuarios")
        {
            if (values["PasswordHash"] == DBNull.Value) throw new InvalidOperationException("Asigna una contraseña al usuario.");
            if (existing != null && existing.Field<bool>("Activo") && existing.Field<string>("Rol") == "Administrador" && (!(bool)values["Activo"] || (string)values["Rol"] != "Administrador") && ActiveAdmins() <= 1)
                throw new InvalidOperationException("Debe quedar al menos un administrador activo.");
        }
    }
    private void Persist()
    {
        if (IsDemo)
        {
            Directory.CreateDirectory(demoDirectory);
            string temporary = DemoPath + ".tmp";
            Data.WriteXml(temporary, XmlWriteMode.IgnoreSchema);
            File.Move(temporary, DemoPath, true);
            Data.AcceptChanges(); return;
        }
        using var connection = new SqlConnection(settings.ConnectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        try
        {
            // Force every write through one lock: protects the last administrator across sessions.
            using (var check = new SqlCommand("DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=N'InventarioWrite', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=8000; SELECT @r", connection, transaction))
                if (Convert.ToInt32(check.ExecuteScalar()) < 0) throw new InvalidOperationException("Otro usuario está guardando. Espera unos segundos y vuelve a intentarlo.");
            foreach (DataTable table in Data.Tables)
            {
                if (table.GetChanges() == null) continue;
                using var adapter = new SqlDataAdapter(new SqlCommand("SELECT * FROM dbo.[" + table.TableName + "]", connection, transaction));
                using var builder = new SqlCommandBuilder(adapter) { ConflictOption = ConflictOption.CompareAllSearchableValues };
                adapter.AcceptChangesDuringUpdate = false;
                adapter.Update(table);
            }
            using var admins = new SqlCommand("SELECT COUNT(*) FROM dbo.Usuarios WHERE Activo=1 AND Rol=N'Administrador'", connection, transaction);
            if (Convert.ToInt32(admins.ExecuteScalar()) == 0) throw new InvalidOperationException("Debe quedar un administrador activo. Actualiza la lista y vuelve a intentarlo.");
            transaction.Commit(); Data.AcceptChanges();
        }
        catch { transaction.Rollback(); throw; }
    }
    public DataRow? Authenticate(string login, string password) => Table("Usuarios").AsEnumerable().FirstOrDefault(r => r.Field<bool>("Activo") && string.Equals(r.Field<string>("Login"), login.Trim(), StringComparison.OrdinalIgnoreCase) && Passwords.Verify(password, r.Field<string>("PasswordHash")!));
    public string Display(string table, object id)
    {
        var row = id == DBNull.Value ? null : Table(table).Rows.Find(id);
        if (row == null) return "";
        return table == "Equipos" ? row["Codigo"] + " · " + row["Marca"] + " " + row["Modelo"] : Convert.ToString(row["Nombre"])!;
    }
    public DataTable View(Module module)
    {
        var output = new DataTable(); output.Columns.Add("Id", typeof(Guid));
        foreach (var f in module.Fields) output.Columns.Add(f.Label, f.Lookup == null ? Table(module.Table).Columns[f.Name]!.DataType : typeof(string));
        foreach (DataRow row in Table(module.Table).Rows)
        {
            var values = new List<object> { row["Id"] };
            values.AddRange(module.Fields.Select(f => f.Lookup != null ? Display(f.Lookup, row[f.Name]) : row[f.Name]));
            output.Rows.Add(values.ToArray());
        }
        return output;
    }
    private static void Seed(DataSet data)
    {
        var locations = data.Tables["Ubicaciones"]!;
        Guid lab1=Guid.NewGuid(), lab2=Guid.NewGuid(), office=Guid.NewGuid();
        locations.Rows.Add(lab1,"Laboratorio 1","Sala de cómputo"); locations.Rows.Add(lab2,"Laboratorio 2","Sala de prácticas"); locations.Rows.Add(office,"Secretaría","Oficina administrativa");
        Guid person=Guid.NewGuid(); data.Tables["Responsables"]!.Rows.Add(person,"Responsable de laboratorio","","Encargado","");
        var eq = data.Tables["Equipos"]!;
        string[][] examples = [["EQ-001","Computadora","HP","ProDesk 400","SN-001"],["EQ-002","Computadora","Dell","OptiPlex 7060","SN-002"],["EQ-003","Monitor","LG","24MP400","SN-003"],["EQ-004","Impresora","Epson","EcoTank L3250","SN-004"],["EQ-005","Teclado","Logitech","K120","SN-005"]];
        for(int i=0;i<examples.Length;i++) { var r=eq.NewRow(); r["Id"]=Guid.NewGuid(); for(int j=0;j<5;j++) r[new[]{"Codigo","Tipo","Marca","Modelo","Serie"}[j]]=examples[i][j]; r["Procesador"]=i<2?"Intel Core i5":""; r["Ram"]=i<2?"8 GB":""; r["Almacenamiento"]=i<2?"256 GB SSD":""; r["UbicacionId"]=i==3?office:i==2?lab2:lab1; r["ResponsableId"]=person; r["Estado"]="Operativo"; r["Observaciones"]="Dato de ejemplo"; eq.Rows.Add(r); }
    }
}
