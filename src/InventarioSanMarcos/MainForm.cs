using System.Data;

namespace InventarioSanMarcos;

internal sealed class MainForm : Form
{
    private readonly Store store;
    private readonly Guid userId;
    private readonly Panel body = new() { Dock = DockStyle.Fill, BackColor = Ui.Background, Padding = new Padding(26) };
    private readonly ToolStripStatusLabel status = new();
    public bool LogoutRequested { get; private set; }
    private bool IsAdmin => CurrentUser.Field<string>("Rol") == "Administrador";
    private DataRow CurrentUser => store.Table("Usuarios").Rows.Find(userId) ?? throw new InvalidOperationException("La cuenta ya no existe. Vuelve a iniciar sesión.");
    public MainForm(Store store, DataRow user)
    {
        this.store = store; userId = user.Field<Guid>("Id");
        Text = "Inventario de equipos — IESTP San Marcos"; Font = new Font("Segoe UI", 10);
        MinimumSize = new Size(1020, 720); Size = new Size(1240, 850); StartPosition = FormStartPosition.CenterScreen;
        var menu = new MenuStrip { BackColor = Color.White, Padding = new Padding(18, 4, 0, 4) };
        menu.Items.Add("Inicio", null, (_,_) => Home());
        foreach (var module in Catalog.Modules)
        {
            var item = menu.Items.Add(module.Title, null, (_,_) => ShowModule(module));
            if (module.Table == "Usuarios") item.Enabled = IsAdmin;
        }
        menu.Items.Add("Reportes", null, (_,_) => ShowReports());
        menu.Items.Add("Cerrar sesión", null, (_,_) => { LogoutRequested = true; Close(); });
        var strip = new StatusStrip { BackColor = Color.White }; strip.Items.Add(status);
        Controls.Add(body); Controls.Add(Ui.Header("Sistema de inventario de equipos y periféricos")); Controls.Add(menu); Controls.Add(strip); MainMenuStrip = menu;
        SetStatus(); Home();
    }
    private void SetStatus() => status.Text = "Usuario: " + CurrentUser["Login"] + "  ·  " + CurrentUser["Rol"] + "     |     " + (store.IsDemo ? "DEMOSTRACIÓN · datos locales de ejemplo" : "SQL Server") + "     |     " + store.Table("Equipos").Rows.Count + " equipos";
    private void ClearBody() { foreach (Control c in body.Controls.Cast<Control>().ToArray()) c.Dispose(); body.Controls.Clear(); }
    private void RefreshStore()
    {
        store.Reload();
        if (!(bool)CurrentUser["Activo"]) { LogoutRequested = true; Close(); throw new InvalidOperationException("Tu cuenta fue desactivada."); }
        SetStatus();
    }
    private void Home()
    {
        ClearBody();
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 5 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 65));
        for (int i=0; i<3; i++) layout.RowStyles.Add(new RowStyle(SizeType.Percent, 33.33f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        var title = Ui.Label("Administración del inventario", 22, true); layout.Controls.Add(title, 0, 0); layout.SetColumnSpan(title, 2);
        var modules = new[] { Catalog.Modules[0], Catalog.Modules[1], Catalog.Modules[2], Catalog.Modules[3], null, Catalog.Modules[4] };
        for(int i=0;i<modules.Length;i++)
        {
            var m = modules[i];
            var b = Ui.Button(m?.Title ?? "Reportes", (_,_) => { if (m == null) ShowReports(); else ShowModule(m); });
            b.Dock = DockStyle.Fill; b.Margin = new Padding(0, 0, 18, 18); b.Font = new Font("Segoe UI", 15, FontStyle.Bold);
            b.Text += Environment.NewLine + (m == null ? "Consultar, exportar e imprimir" : m.Description);
            b.TextAlign = ContentAlignment.MiddleLeft; b.Padding = new Padding(24);
            b.Enabled = m?.Table != "Usuarios" || IsAdmin;
            layout.Controls.Add(b, i%2, i/2+1);
        }
        var info = Ui.Label(store.IsDemo ? "Modo demostración: puedes practicar con datos de ejemplo. Los cambios se guardan en este equipo." : "Selecciona un módulo para consultar y actualizar el inventario.", 10);
        layout.Controls.Add(info,0,4); layout.SetColumnSpan(info,2); body.Controls.Add(layout);
    }
    internal void ShowModule(Module module)
    {
        if (module.Table == "Usuarios" && !IsAdmin) { Ui.Error(new InvalidOperationException("Este módulo es exclusivo para administradores.")); return; }
        ClearBody();
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56)); layout.RowStyles.Add(new RowStyle(SizeType.Percent,100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute,28));
        layout.Controls.Add(Ui.Label(module.Title, 23, true),0,0);
        var tools = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        var grid = Ui.Grid(); var count = Ui.Label("");
        var search = new TextBox { Width = 210, PlaceholderText = "Buscar en la tabla…", Margin = new Padding(0,7,14,0) };
        DataView? view = null;
        void LoadRows() { view = new DataView(store.View(module)); grid.DataSource = view; Ui.HideId(grid); Filter(); }
        void Filter()
        {
            if (view == null) return;
            string q=search.Text.Trim();
            // Literal matching avoids expression injection and LIKE wildcard surprises.
            var source=store.View(module); var filtered=source.Clone();
            foreach(DataRow row in source.Rows)
                if(q.Length==0 || source.Columns.Cast<DataColumn>().Where(c=>c.ColumnName!="Id").Any(c=>Convert.ToString(row[c])!.Contains(q,StringComparison.OrdinalIgnoreCase))) filtered.ImportRow(row);
            grid.DataSource=filtered; Ui.HideId(grid);
            if(module.Table=="Equipos")
                foreach(string detail in new[]{"N.º de serie","Procesador","Memoria RAM","Almacenamiento","Observaciones"})
                    grid.Columns[detail]!.Visible=false;
            grid.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill;
            foreach(DataGridViewColumn column in grid.Columns)if(column.Visible)column.MinimumWidth=85;
            count.Text=filtered.Rows.Count+" registros · Doble clic para ver o editar los detalles";
        }
        DataRow Selected()
        {
            if(grid.CurrentRow?.DataBoundItem is not DataRowView selected) throw new InvalidOperationException("Selecciona un registro en la tabla.");
            return store.Table(module.Table).Rows.Find(selected.Row["Id"]) ?? throw new InvalidOperationException("El registro ya no existe. Actualiza la tabla.");
        }
        void Edit(bool isNew)
        {
            try
            {
                Guid? selectedId=isNew?null:Selected().Field<Guid>("Id");
                RefreshStore(); if (module.Table == "Usuarios" && !IsAdmin) throw new InvalidOperationException("No tienes permiso para administrar usuarios.");
                DataRow? row = isNew?null:store.Table(module.Table).Rows.Find(selectedId!);
                if (!isNew && row == null) throw new InvalidOperationException("El registro fue eliminado. Actualiza la lista.");
                using var editor = new EditorForm(store, module, row, userId);
                if(editor.ShowDialog(this)==DialogResult.OK) { SetStatus(); LoadRows(); }
            }
            catch(Exception ex) { Ui.Error(ex); }
        }
        tools.Controls.Add(search);
        tools.Controls.Add(Ui.Button("Nuevo",(_,_)=>Edit(true),true));
        tools.Controls.Add(Ui.Button("Editar",(_,_)=>Edit(false)));
        var delete = Ui.Button("Eliminar",(_,_)=>
        {
            try
            {
                Guid id=Selected().Field<Guid>("Id"); RefreshStore(); if(!IsAdmin) throw new InvalidOperationException("Solo un administrador puede eliminar registros.");
                var row=store.Table(module.Table).Rows.Find(id) ?? throw new InvalidOperationException("El registro ya no existe.");
                if(module.Table=="Usuarios" && id==userId) throw new InvalidOperationException("No puedes eliminar tu propia sesión.");
                if(MessageBox.Show(this,"¿Eliminar el registro seleccionado? Esta acción no se puede deshacer.","Confirmar eliminación",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)!=DialogResult.Yes) return;
                store.Delete(module.Table,row); SetStatus(); LoadRows();
            } catch(Exception ex) { Ui.Error(ex); }
        }); delete.Enabled=IsAdmin; tools.Controls.Add(delete);
        tools.Controls.Add(Ui.Button("Actualizar",(_,_)=> {try {RefreshStore(); LoadRows();}catch(Exception ex){Ui.Error(ex);}}));
        if(module.Table!="Usuarios") tools.Controls.Add(Ui.Button("Excel",(_,_)=> {try{Export.Save(this,Export.VisibleTable(grid),module.Title);}catch(Exception ex){Ui.Error(ex);}}));
        layout.Controls.Add(tools,0,1); layout.Controls.Add(grid,0,2); layout.Controls.Add(count,0,3); body.Controls.Add(layout);
        search.TextChanged+=(_,_)=>Filter(); grid.CellDoubleClick+=(_,e)=>{if(e.RowIndex>=0)Edit(false);}; LoadRows();
    }
    internal void ShowReports()
    {
        ClearBody(); body.Controls.Add(new ReportsView(store) { Dock=DockStyle.Fill });
    }
}

internal sealed class EditorForm : Form
{
    public EditorForm(Store store, Module module, DataRow? row, Guid sessionUser)
    {
        Text=(row==null?"Nuevo registro · ":"Editar · ")+module.Title;
        Font=new Font("Segoe UI",10); StartPosition=FormStartPosition.CenterParent; Size=new Size(870, module.Fields.Length>8?720:580); MinimumSize=new Size(760,500); BackColor=Ui.Background;
        var layout=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=2,Padding=new Padding(24),AutoScroll=true };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,50));
        Controls.Add(layout); var controls=new Dictionary<string,Control>();
        int index=0;
        foreach(var f in module.Fields)
        {
            var group=new TableLayoutPanel { Dock=DockStyle.Top, AutoSize=true,ColumnCount=1,Margin=new Padding(0,0,20,8) };
            group.Controls.Add(Ui.Label(f.Label+(f.Required?" *":"")));
            Control input;
            object value=row?[f.Name]??DBNull.Value;
            if(f.Kind=="lookup")
            {
                var choices=new DataTable(); choices.Columns.Add("Id",typeof(Guid)); choices.Columns.Add("Nombre");
                choices.Rows.Add(Guid.Empty,f.Required?"Selecciona…":"Sin asignar");
                foreach(DataRow item in store.Table(f.Lookup!).Rows) choices.Rows.Add(item["Id"],store.Display(f.Lookup!,item["Id"]));
                var combo=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,DataSource=choices,DisplayMember="Nombre",ValueMember="Id",Dock=DockStyle.Top};
                object selectedValue=value==DBNull.Value?Guid.Empty:value;
                Shown += (_,_) => combo.SelectedValue=selectedValue;
                combo.SelectedValue=selectedValue; input=combo;
            }
            else if(f.Kind=="choice") {var combo=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Dock=DockStyle.Top};combo.Items.AddRange(f.Choices!); combo.SelectedIndex=0;if(value!=DBNull.Value)combo.SelectedItem=value;input=combo;}
            else if(f.Kind=="date") input=new DateTimePicker{Format=DateTimePickerFormat.Short,Value=value==DBNull.Value?DateTime.Today:(DateTime)value,Dock=DockStyle.Top};
            else if(f.Kind=="money") input=new NumericUpDown{DecimalPlaces=2,Maximum=99999999,Value=value==DBNull.Value?0:(decimal)value,Dock=DockStyle.Top};
            else if(f.Kind=="bool") input=new CheckBox{Text="Cuenta habilitada",Checked=value==DBNull.Value || (bool)value,AutoSize=true};
            else input=new TextBox{Text=Convert.ToString(value),Dock=DockStyle.Top,MaxLength=f.Kind=="notes"?1000:150,Multiline=f.Kind=="notes",Height=f.Kind=="notes"?70:28};
            if(module.Table=="Usuarios" && row?.Field<Guid>("Id")==sessionUser && f.Name is "Rol" or "Activo") input.Enabled=false;
            input.AccessibleName=f.Label; controls.Add(f.Name,input); group.Controls.Add(input);layout.Controls.Add(group,index%2,index/2); index++;
        }
        TextBox? password=null;
        if(module.Table=="Usuarios")
        {
            var group=new TableLayoutPanel{AutoSize=true,Dock=DockStyle.Top,ColumnCount=1,Margin=new Padding(0,0,20,8)};
            group.Controls.Add(Ui.Label(row==null?"Contraseña (mínimo 10 caracteres) *":"Nueva contraseña (vacía para conservar)")); password=new TextBox{UseSystemPasswordChar=true,Dock=DockStyle.Top}; group.Controls.Add(password); layout.Controls.Add(group,index%2,index/2);index++;
        }
        var actions=new FlowLayoutPanel{AutoSize=true,Dock=DockStyle.Top,Padding=new Padding(0,15,0,0)};
        var save=Ui.Button("Guardar",(_,_)=>
        {
            try
            {
                var values=new Dictionary<string,object>();
                foreach(var f in module.Fields)
                {
                    var c=controls[f.Name];
                    values[f.Name]=f.Kind switch {
                        "lookup"=>((ComboBox)c).SelectedValue is Guid id && id!=Guid.Empty?id:DBNull.Value,
                        "choice"=>((ComboBox)c).SelectedItem??DBNull.Value,
                        "date"=>((DateTimePicker)c).Value.Date,
                        "money"=>((NumericUpDown)c).Value,
                        "bool"=>((CheckBox)c).Checked,
                        _=>string.IsNullOrWhiteSpace(c.Text) && !f.Required?DBNull.Value:c.Text.Trim()
                    };
                }
                if(password!=null && (row==null || password.Text.Length>0))
                {if(password.Text.Length<10)throw new InvalidOperationException("La contraseña debe tener al menos 10 caracteres."); values["PasswordHash"]=Passwords.Hash(password.Text);}
                store.Save(module.Table,row,values); DialogResult=DialogResult.OK;
            } catch(Exception ex) {Ui.Error(ex);}
        },true);
        actions.Controls.Add(save); var cancel=Ui.Button("Cancelar",(_,_)=>DialogResult=DialogResult.Cancel);actions.Controls.Add(cancel);
        layout.Controls.Add(actions,0,(index+1)/2);layout.SetColumnSpan(actions,2);AcceptButton=save;CancelButton=cancel;
    }
}
