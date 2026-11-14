using System.Data;
using System.Globalization;

namespace InventarioSanMarcos;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("es-PE");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("es-PE");
        if (args.Contains("--self-test")) { SelfTest.Run(); return; }
        if (args.Contains("--preview") || args.Contains("--capture"))
        {
            string temporary = Path.Combine(Path.GetTempPath(), "InventarioPreview-"+Guid.NewGuid().ToString("N"));
            try
            {
                var preview = new Store(new Settings(), temporary);
                preview.Save("Usuarios", null, new() { ["Nombre"]="Vista de demostración", ["Login"]="demo", ["Rol"]="Administrador", ["Activo"]=true, ["PasswordHash"]=Passwords.Hash(Guid.NewGuid().ToString()) });
                if(args.Contains("--capture"))
                {
                    string target=args.Last();Directory.CreateDirectory(target);
                    using var main=new MainForm(preview,preview.Table("Usuarios").Rows[0]);
                    void Capture(Form form,string name) { form.Show(); form.Refresh(); Application.DoEvents(); using var bitmap=new Bitmap(form.Width,form.Height);form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,form.Size));bitmap.Save(Path.Combine(target,name+".png")); }
                    Capture(main,"01-inicio");main.ShowModule(Catalog.Modules[0]);Capture(main,"02-equipos");main.ShowReports();Capture(main,"03-reportes");
                    using var editor=new EditorForm(preview,Catalog.Modules[0],preview.Table("Equipos").Rows[0],preview.Table("Usuarios").Rows[0].Field<Guid>("Id"));Capture(editor,"04-editar-equipo");editor.Close();main.Close();
                    using var login=new LoginForm(new Store(new Settings(),Path.Combine(temporary,"login")),new Settings());Capture(login,"05-primer-acceso");login.Close();
                }
                else Application.Run(new MainForm(preview, preview.Table("Usuarios").Rows[0]));
            }
            finally { if(Directory.Exists(temporary)) Directory.Delete(temporary, true); }
            return;
        }
        Application.ThreadException += (_, e) => Ui.Error(e.Exception);
        try
        {
            var settings = Settings.Load();
            while (true)
            {
                Store store;
                try { store = new Store(settings); }
                catch (Exception ex)
                {
                    Ui.Error(new InvalidOperationException("No se pudo abrir el inventario. Revisa el servidor y ejecuta database/01-schema.sql si aún no creaste las tablas.", ex));
                    using var configuration = new ConfigurationForm(settings);
                    if (configuration.ShowDialog() != DialogResult.OK) return;
                    continue;
                }
                using var login = new LoginForm(store, settings);
                if (login.ShowDialog() == DialogResult.Retry) continue;
                if (login.DialogResult != DialogResult.OK || login.User == null) return;
                using var main = new MainForm(store, login.User);
                Application.Run(main);
                if (!main.LogoutRequested) return;
            }
        }
        catch (Exception ex) { Ui.Error(ex); }
    }
}

internal static class Ui
{
    public static readonly Color Navy = Color.FromArgb(12, 54, 99);
    public static readonly Color Blue = Color.FromArgb(0, 105, 182);
    public static readonly Color Background = Color.FromArgb(242, 245, 249);
    public static Button Button(string text, EventHandler action, bool primary = false)
    {
        var b = new Button { Text = text, AutoSize = true, Height = 38, MinimumSize = new Size(105, 38), Padding = new Padding(12, 4, 12, 4), FlatStyle = FlatStyle.Flat, BackColor = primary ? Blue : Color.White, ForeColor = primary ? Color.White : Navy, Cursor = Cursors.Hand, Margin = new Padding(0, 0, 10, 0) };
        b.FlatAppearance.BorderColor = primary ? Blue : Color.FromArgb(195, 207, 220);
        b.Click += action; return b;
    }
    public static Label Label(string text, float size = 10, bool bold = false) => new() { Text = text, AutoSize = true, ForeColor = Navy, Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular), Margin = new Padding(3, 6, 3, 4) };
    public static Panel Header(string subtitle)
    {
        var p = new Panel { Dock = DockStyle.Top, Height = 112, BackColor = Color.White, Padding = new Padding(24, 10, 24, 10) };
        var logo = new LogoView { Location = new Point(24, 8), Size = new Size(78, 94) }; p.Controls.Add(logo);
        var title = Label("IESTP SAN MARCOS", 20, true); title.Location = new Point(124, 23); p.Controls.Add(title);
        var caption = Label(subtitle, 11); caption.Location = new Point(126, 63); p.Controls.Add(caption);
        return p;
    }
    public static void Error(Exception ex)
    {
        string message = ex switch
        {
            DBConcurrencyException => "Otro usuario modificó este registro. Actualiza la lista antes de volver a editarlo.",
            ConstraintException => "Hay un código, usuario o nombre duplicado, o un registro relacionado que ya no existe.",
            Microsoft.Data.SqlClient.SqlException sql when sql.Number is 2601 or 2627 => "Ya existe un registro con ese código, usuario o número de serie.",
            Microsoft.Data.SqlClient.SqlException sql when sql.Number == 547 => "El registro está en uso o contiene una referencia no válida.",
            Microsoft.Data.SqlClient.SqlException => "No se pudo guardar o consultar SQL Server. Verifica la conexión y actualiza los datos antes de reintentar.",
            _ => ex.Message
        };
        MessageBox.Show(message, "Inventario de equipos", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
    public static DataGridView Grid() => new()
    {
        Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
        MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells,
        BackgroundColor = Color.White, BorderStyle = BorderStyle.None, RowHeadersVisible = false, EnableHeadersVisualStyles = false,
        ColumnHeadersHeight = 40, ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
        ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = Navy, ForeColor = Color.White, SelectionBackColor = Navy, SelectionForeColor = Color.White, Font = new Font("Segoe UI", 10, FontStyle.Bold), Padding = new Padding(8, 0, 8, 0) },
        DefaultCellStyle = new DataGridViewCellStyle { Padding = new Padding(8, 4, 8, 4), SelectionBackColor = Color.FromArgb(211, 232, 250), SelectionForeColor = Navy },
        AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(241, 247, 252) }, RowTemplate = { Height = 34 }
    };
    public static void HideId(DataGridView grid) { if (grid.Columns["Id"] is DataGridViewColumn column) column.Visible = false; }
}

internal sealed class LogoView : Control
{
    private readonly Image? image;
    public LogoView() { DoubleBuffered = true; string path = Path.Combine(AppContext.BaseDirectory, "Assets", "san-marcos.png"); if (File.Exists(path)) image = Image.FromFile(path); }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); if (image == null) return;
        // The supplied artwork has a wide transparent canvas; display the crest region.
        var source = new RectangleF(0, 0, image.Width * .29f, image.Height);
        float scale = Math.Min(Width / source.Width, Height / source.Height);
        var target = new RectangleF((Width-source.Width*scale)/2,(Height-source.Height*scale)/2,source.Width*scale,source.Height*scale);
        e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        e.Graphics.DrawImage(image, target, source, GraphicsUnit.Pixel);
    }
    protected override void Dispose(bool disposing) { if (disposing) image?.Dispose(); base.Dispose(disposing); }
}

internal sealed class ConfigurationForm : Form
{
    public ConfigurationForm(Settings settings)
    {
        Text = "Origen de datos"; ClientSize = new Size(680, 330); Font = new Font("Segoe UI", 10); StartPosition = FormStartPosition.CenterParent; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 6 }; Controls.Add(layout);
        layout.Controls.Add(Ui.Label("Selecciona dónde guardar el inventario", 15, true));
        var mode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top }; mode.Items.AddRange(["Demostración local", "SQL Server"]); mode.SelectedIndex = settings.Mode == "Demo" ? 0 : 1; layout.Controls.Add(mode);
        layout.Controls.Add(Ui.Label("Cadena de conexión (SQL Server)"));
        var connection = new TextBox { Text = settings.ConnectionString, Dock = DockStyle.Top, Multiline = true, Height = 65, UseSystemPasswordChar = false }; layout.Controls.Add(connection);
        layout.Controls.Add(new Label { Text = "Demo y SQL mantienen datos separados. Para SQL, crea primero la base con el archivo database/01-schema.sql. Se recomienda autenticación de Windows.", Dock = DockStyle.Fill, AutoSize = true });
        var save = Ui.Button("Guardar", (_,_) => { try { if (mode.SelectedIndex == 1) _ = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connection.Text); settings.Mode = mode.SelectedIndex == 0 ? "Demo" : "SqlServer"; settings.ConnectionString = connection.Text.Trim(); settings.Save(); DialogResult = DialogResult.OK; } catch (Exception ex) { Ui.Error(ex); } }, true);
        layout.Controls.Add(save); AcceptButton = save;
    }
}

internal sealed class LoginForm : Form
{
    public DataRow? User { get; private set; }
    private int failures;
    private DateTime retryAt;
    public LoginForm(Store store, Settings settings)
    {
        bool first = store.Table("Usuarios").Rows.Count == 0;
        Text = first ? "Crear administrador · San Marcos" : "Iniciar sesión · San Marcos";
        ClientSize = new Size(600, first ? 520 : 470); Font = new Font("Segoe UI", 10); StartPosition = FormStartPosition.CenterScreen; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false;
        Controls.Add(Ui.Header("Sistema de inventario de equipos"));
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(40, 10, 40, 20) }; Controls.Add(layout); layout.BringToFront();
        var header = Controls.OfType<Panel>().First(); Controls.SetChildIndex(header, 0);
        layout.Controls.Add(Ui.Label(first ? "Configura tu administrador" : "Bienvenido", 16, true));
        layout.Controls.Add(Ui.Label(store.IsDemo ? "MODO DEMOSTRACIÓN · datos de ejemplo" : "Conectado a SQL Server", 10));
        layout.Controls.Add(Ui.Label("Usuario")); var login = new TextBox { Dock = DockStyle.Top, MaxLength = 150, Text = first ? "admin" : "" }; layout.Controls.Add(login);
        layout.Controls.Add(Ui.Label(first ? "Contraseña (mínimo 10 caracteres)" : "Contraseña")); var password = new TextBox { Dock = DockStyle.Top, UseSystemPasswordChar = true }; layout.Controls.Add(password);
        TextBox? repeat = null;
        if (first) { layout.Controls.Add(Ui.Label("Repite la contraseña")); repeat = new TextBox { Dock = DockStyle.Top, UseSystemPasswordChar = true }; layout.Controls.Add(repeat); }
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(0, 12, 0, 0) }; layout.Controls.Add(buttons);
        var enter = Ui.Button(first ? "Crear e ingresar" : "Ingresar", (_,_) =>
        {
            try
            {
                if (DateTime.Now < retryAt) throw new InvalidOperationException("Espera 30 segundos antes de volver a intentar.");
                if (string.IsNullOrWhiteSpace(login.Text)) throw new InvalidOperationException("Escribe tu usuario.");
                if (first)
                {
                    if (password.Text.Length < 10 || password.Text != repeat!.Text) throw new InvalidOperationException("Las contraseñas deben coincidir y tener al menos 10 caracteres.");
                    store.Save("Usuarios", null, new() { ["Nombre"] = "Administrador", ["Login"] = login.Text.Trim(), ["Rol"] = "Administrador", ["Activo"] = true, ["PasswordHash"] = Passwords.Hash(password.Text) });
                }
                User = store.Authenticate(login.Text, password.Text);
                if (User == null) { failures++; if (failures >= 5) { retryAt = DateTime.Now.AddSeconds(30); failures = 0; } throw new InvalidOperationException("Usuario o contraseña incorrectos, o cuenta inactiva."); }
                DialogResult = DialogResult.OK;
            }
            catch (Exception ex) { Ui.Error(ex); }
        }, true);
        buttons.Controls.Add(enter); AcceptButton = enter;
        buttons.Controls.Add(Ui.Button("Origen de datos", (_,_) => { using var c = new ConfigurationForm(settings); if (c.ShowDialog(this) == DialogResult.OK) DialogResult = DialogResult.Retry; }));
    }
}
