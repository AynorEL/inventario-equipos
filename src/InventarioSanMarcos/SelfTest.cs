using System.Data;
using System.IO.Compression;
using System.Xml.Linq;

namespace InventarioSanMarcos;

internal static class SelfTest
{
    public static void Run()
    {
        string directory=Path.Combine(Path.GetTempPath(),"InventarioTest-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        var results=new List<string>();
        void Check(bool condition,string name){if(!condition)throw new Exception("FALLÓ: "+name);results.Add("OK · "+name);}
        void Reject(Action action,string name){bool rejected=false;try{action();}catch(InvalidOperationException){rejected=true;}catch(ConstraintException){rejected=true;}Check(rejected,name);}
        try
        {
            var store=new Store(new Settings(),directory);
            Check(store.Table("Equipos").Rows.Count==5,"Datos de demostración separados");
            string hash=Passwords.Hash("Prueba-segura-123");Check(Passwords.Verify("Prueba-segura-123",hash)&&!Passwords.Verify("incorrecta",hash),"Contraseñas con hash y verificación");
            store.Save("Usuarios",null,new(){["Nombre"]="Pruebas",["Login"]="admin",["Rol"]="Administrador",["Activo"]=true,["PasswordHash"]=hash});
            Check(store.Authenticate("ADMIN","Prueba-segura-123")!=null,"Autenticación sin distinguir mayúsculas");
            var admin=store.Table("Usuarios").Rows[0];Reject(()=>store.Delete("Usuarios",admin),"Protección del último administrador");
            Reject(()=>store.Save("Usuarios",admin,new(){["Activo"]=false}),"No desactivar el último administrador");
            Reject(()=>store.Delete("Ubicaciones",store.Table("Ubicaciones").Rows[0]),"Proteger ubicaciones asignadas");
            var equipment=store.Table("Equipos").Rows[0];
            Reject(()=>store.Save("Equipos",equipment,new(){["Codigo"]="EQ-002"}),"Código de equipo único");
            Reject(()=>store.Save("Equipos",equipment,new(){["Serie"]="SN-002"}),"Número de serie único");
            store.Save("Equipos",equipment,new(){["Observaciones"]="Texto de prueba con ñ y tildes"});
            store=new Store(new Settings(),directory);Check(store.Table("Equipos").Rows[0].Field<string>("Observaciones")=="Texto de prueba con ñ y tildes","Guardar y recuperar cambios");
            using(var editor=new EditorForm(store,Catalog.Modules[0],store.Table("Equipos").Rows[0],admin.Field<Guid>("Id")))
            {
                editor.Show(); Application.DoEvents();
                IEnumerable<Control> Descendants(Control parent)=>parent.Controls.Cast<Control>().SelectMany(c=>new[]{c}.Concat(Descendants(c)));
                var combo=Descendants(editor).OfType<ComboBox>().First(c=>c.AccessibleName=="Ubicación");
                Check(combo.SelectedValue is Guid selected && selected==store.Table("Equipos").Rows[0].Field<Guid>("UbicacionId"),"La edición conserva la ubicación asignada");editor.Close();
            }
            store.Save("Ubicaciones",null,new(){["Nombre"]="Aula nueva",["Descripcion"]=DBNull.Value});
            var added=store.Table("Ubicaciones").AsEnumerable().First(r=>r.Field<string>("Nombre")=="Aula nueva");store.Delete("Ubicaciones",added);
            Check(!store.Table("Ubicaciones").AsEnumerable().Any(r=>r.Field<string>("Nombre")=="Aula nueva"),"Crear y eliminar registros sin dependencias");
            var data=new DataTable();data.Columns.Add("Código");data.Columns.Add("Observación");data.Columns.Add("Costo",typeof(decimal));data.Rows.Add("001","=HYPERLINK(\"x\") & áéíóú",12.50m);
            string path=Path.Combine(directory,"prueba.xlsx");Export.Write(path,data,"Prueba");
            using(var archive=ZipFile.OpenRead(path))
            {
                foreach(var entry in archive.Entries){using var stream=entry.Open();_ = XDocument.Load(stream);}
                using var sheet=archive.GetEntry("xl/worksheets/sheet1.xml")!.Open();var xml=XDocument.Load(sheet);XNamespace ns="http://schemas.openxmlformats.org/spreadsheetml/2006/main";
                Check(!xml.Descendants(ns+"f").Any()&&xml.Descendants(ns+"t").Any(t=>t.Value=="001")&&xml.Descendants(ns+"v").Any(v=>v.Value=="12.50"),"Excel válido: códigos con ceros, decimales y texto sin fórmulas");
            }
            results.Add("Pruebas locales completadas. SQL Server requiere una instancia para la prueba de integración.");
            File.WriteAllLines(Path.Combine(AppContext.BaseDirectory,"pruebas.txt"),results);
        }
        catch(Exception ex){results.Add(ex.ToString());File.WriteAllLines(Path.Combine(AppContext.BaseDirectory,"pruebas.txt"),results);Environment.ExitCode=1;}
        finally{Directory.Delete(directory,true);}
    }
}
