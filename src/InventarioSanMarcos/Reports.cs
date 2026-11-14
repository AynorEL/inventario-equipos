using System.Data;
using System.Drawing.Printing;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace InventarioSanMarcos;

internal sealed class ReportsView : UserControl
{
    private readonly Store store;
    private readonly ComboBox report = new() { DropDownStyle=ComboBoxStyle.DropDownList,Width=170 };
    private readonly ComboBox location = new() { DropDownStyle=ComboBoxStyle.DropDownList,Width=170 };
    private readonly ComboBox type = new() { DropDownStyle=ComboBoxStyle.DropDownList,Width=150 };
    private readonly ComboBox state = new() { DropDownStyle=ComboBoxStyle.DropDownList,Width=140 };
    private readonly DataGridView grid = Ui.Grid();
    private readonly Label total = Ui.Label("");
    public ReportsView(Store store)
    {
        this.store=store;
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=5};
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,55));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,90));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,52));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,30));
        layout.Controls.Add(Ui.Label("Reportes de inventario",23,true),0,0);
        var filters=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=true};
        report.Items.AddRange(["Inventario de equipos","Mantenimiento"]);report.SelectedIndex=0;
        location.Items.Add("Todas");foreach(DataRow r in store.Table("Ubicaciones").Rows)location.Items.Add(r["Nombre"].ToString()!);location.SelectedIndex=0;
        type.Items.Add("Todos");type.Items.AddRange(Catalog.Types);type.SelectedIndex=0;
        state.Items.Add("Todos");state.Items.AddRange(Catalog.States);state.SelectedIndex=0;
        foreach(var pair in new[]{("Reporte",report),("Ubicación",location),("Tipo",type),("Estado",state)})
        {var p=new FlowLayoutPanel{FlowDirection=FlowDirection.TopDown,Width=pair.Item2.Width+12,Height=85};p.Controls.Add(Ui.Label(pair.Item1));p.Controls.Add(pair.Item2);filters.Controls.Add(p);}
        report.SelectedIndexChanged+=(_,_)=>{bool inventory=report.SelectedIndex==0;location.Enabled=inventory;type.Enabled=inventory;state.Items.Clear();state.Items.Add("Todos");state.Items.AddRange(inventory?Catalog.States:["Pendiente","En proceso","Finalizado"]);state.SelectedIndex=0;Generate();};
        layout.Controls.Add(filters,0,1);
        var actions=new FlowLayoutPanel{Dock=DockStyle.Fill};
        actions.Controls.Add(Ui.Button("Generar reporte",(_,_)=>{try{store.Reload();Generate();}catch(Exception ex){Ui.Error(ex);}},true));
        actions.Controls.Add(Ui.Button("Exportar a Excel",(_,_)=>{try{Export.Save(this,Export.VisibleTable(grid),report.Text);}catch(Exception ex){Ui.Error(ex);}}));
        actions.Controls.Add(Ui.Button("Vista previa / imprimir",(_,_)=>{try{Print(Export.VisibleTable(grid));}catch(Exception ex){Ui.Error(ex);}}));
        layout.Controls.Add(actions,0,2);layout.Controls.Add(grid,0,3);layout.Controls.Add(total,0,4);Controls.Add(layout);Generate();
    }
    private void Generate()
    {
        bool inventory=report.SelectedIndex==0;
        var table=store.View(Catalog.Modules.First(m=>m.Table==(inventory?"Equipos":"Mantenimientos")));
        var result=table.Clone();
        foreach(DataRow row in table.Rows)
        {
            if(inventory && location.SelectedIndex>0 && (string)row["Ubicación"]!=location.Text)continue;
            if(inventory && type.SelectedIndex>0 && (string)row["Tipo"]!=type.Text)continue;
            string stateColumn=inventory?"Estado":"Estado del servicio";
            if(state.SelectedIndex>0 && (string)row[stateColumn]!=state.Text)continue;
            result.ImportRow(row);
        }
        string[] columns=inventory?["Id","Código","Tipo","Marca","Modelo","Ubicación","Responsable","Estado"]:["Id","Equipo","Fecha","Tipo","Técnico","Costo (S/)","Estado del servicio"];
        grid.DataSource=result.DefaultView.ToTable(false,columns);Ui.HideId(grid);
        grid.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill;
        foreach(DataGridViewColumn column in grid.Columns)if(column.Visible)column.MinimumWidth=85;
        total.Text=result.Rows.Count+" registros · "+(store.IsDemo?"Demostración · ":"")+"Generado: "+DateTime.Now.ToString("g");
        total.Tag=report.Text+" · Estado: "+state.Text+(inventory?" · Ubicación: "+location.Text+" · Tipo: "+type.Text:"");
    }
    private void Print(DataTable table)
    {
        if(table.Rows.Count==0)throw new InvalidOperationException("No hay registros para imprimir.");
        using var document=new PrintDocument();document.DocumentName=report.Text;document.DefaultPageSettings.Landscape=true;document.DefaultPageSettings.Margins=new Margins(40,40,40,40);
        int rowIndex=0,page=0;
        document.BeginPrint+=(_,_)=>{rowIndex=0;page=0;};
        document.PrintPage+=(_,e)=>
        {
            var g=e.Graphics!;var area=e.MarginBounds;page++;
            using var heading=new Font("Segoe UI",16,FontStyle.Bold);using var regular=new Font("Segoe UI",8);using var bold=new Font("Segoe UI",8,FontStyle.Bold);
            g.DrawString("IESTP SAN MARCOS",heading,Brushes.Black,area.Left,area.Top);
            g.DrawString(Convert.ToString(total.Tag),regular,Brushes.Black,new RectangleF(area.Left,area.Top+32,area.Width,36));
            g.DrawString(total.Text,regular,Brushes.Black,area.Left,area.Top+65);
            float y=area.Top+90;float width=(float)area.Width/table.Columns.Count;const int rowHeight=40;
            void DrawRow(string[] texts,bool header)
            {
                for(int i=0;i<texts.Length;i++)
                {
                    var box=new RectangleF(area.Left+i*width,y,width,rowHeight);
                    g.FillRectangle(header?Brushes.LightGray:rowIndex%2==0?Brushes.WhiteSmoke:Brushes.White,box);g.DrawRectangle(Pens.Silver,box.X,box.Y,box.Width,box.Height);
                    using var format=new StringFormat{Trimming=StringTrimming.EllipsisCharacter};g.DrawString(texts[i],header?bold:regular,Brushes.Black,new RectangleF(box.X+4,box.Y+5,box.Width-8,box.Height-8),format);
                }
                y+=rowHeight;
            }
            DrawRow(table.Columns.Cast<DataColumn>().Select(c=>c.ColumnName).ToArray(),true);
            while(rowIndex<table.Rows.Count && y+rowHeight<area.Bottom-25)
            {DrawRow(table.Rows[rowIndex].ItemArray.Select(v=>v is DateTime d?d.ToString("d"):Convert.ToString(v)??"").ToArray(),false);rowIndex++;}
            g.DrawString("Página "+page,regular,Brushes.Gray,area.Left,area.Bottom-15);e.HasMorePages=rowIndex<table.Rows.Count;
        };
        using var preview=new PrintPreviewDialog{Document=document,Width=1100,Height=800};preview.ShowDialog(this);
    }
}

internal static class Export
{
    private static readonly XNamespace S="http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    public static DataTable VisibleTable(DataGridView grid)
    {
        var output=new DataTable();var columns=grid.Columns.Cast<DataGridViewColumn>().Where(c=>c.Visible).OrderBy(c=>c.DisplayIndex).ToArray();
        foreach(var c in columns)output.Columns.Add(c.HeaderText,c.ValueType??typeof(string));
        foreach(DataGridViewRow row in grid.Rows)if(!row.IsNewRow)output.Rows.Add(columns.Select(c=>row.Cells[c.Index].Value??DBNull.Value).ToArray());
        return output;
    }
    public static void Save(IWin32Window owner,DataTable table,string title)
    {
        using var dialog=new SaveFileDialog{Filter="Libro de Excel (*.xlsx)|*.xlsx",DefaultExt="xlsx",FileName="Inventario-"+DateTime.Now.ToString("yyyyMMdd-HHmm")+".xlsx"};
        if(dialog.ShowDialog(owner)!=DialogResult.OK)return;
        Write(dialog.FileName,table,title);
        MessageBox.Show(owner,"Archivo Excel guardado correctamente.","Exportación",MessageBoxButtons.OK,MessageBoxIcon.Information);
    }
    public static void Write(string path,DataTable table,string title)
    {
        string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            using(var file=new FileStream(temp,FileMode.CreateNew))
            using(var zip=new ZipArchive(file,ZipArchiveMode.Create))
            {
                void Xml(string name,XElement root){using var stream=zip.CreateEntry(name).Open();new XDocument(new XDeclaration("1.0","utf-8","yes"),root).Save(stream);}
                XNamespace content="http://schemas.openxmlformats.org/package/2006/content-types", rel="http://schemas.openxmlformats.org/package/2006/relationships", office="http://schemas.openxmlformats.org/officeDocument/2006/relationships";
                Xml("[Content_Types].xml",new XElement(content+"Types",new XElement(content+"Default",new XAttribute("Extension","rels"),new XAttribute("ContentType","application/vnd.openxmlformats-package.relationships+xml")),new XElement(content+"Default",new XAttribute("Extension","xml"),new XAttribute("ContentType","application/xml")),new XElement(content+"Override",new XAttribute("PartName","/xl/workbook.xml"),new XAttribute("ContentType","application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml")),new XElement(content+"Override",new XAttribute("PartName","/xl/worksheets/sheet1.xml"),new XAttribute("ContentType","application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"))));
                Xml("_rels/.rels",new XElement(rel+"Relationships",new XElement(rel+"Relationship",new XAttribute("Id","rId1"),new XAttribute("Type",office.NamespaceName+"/officeDocument"),new XAttribute("Target","xl/workbook.xml"))));
                Xml("xl/workbook.xml",new XElement(S+"workbook",new XAttribute(XNamespace.Xmlns+"r",office),new XElement(S+"sheets",new XElement(S+"sheet",new XAttribute("name","Reporte"),new XAttribute("sheetId",1),new XAttribute(office+"id","rId1")))));
                Xml("xl/_rels/workbook.xml.rels",new XElement(rel+"Relationships",new XElement(rel+"Relationship",new XAttribute("Id","rId1"),new XAttribute("Type",office.NamespaceName+"/worksheet"),new XAttribute("Target","worksheets/sheet1.xml"))));
                var rows=new XElement(S+"sheetData");
                string Column(int n){string v="";for(n++;n>0;n=(n-1)/26)v=(char)('A'+(n-1)%26)+v;return v;}
                void Add(int number,IEnumerable<object> values)
                {
                    var row=new XElement(S+"row",new XAttribute("r",number));int col=0;
                    foreach(object value in values)
                    {
                        var cell=new XElement(S+"c",new XAttribute("r",Column(col++)+number));
                        if(value is decimal or double or float or int or long) {cell.Add(new XElement(S+"v",Convert.ToString(value,CultureInfo.InvariantCulture)));}
                        else
                        {
                            string text=value is DateTime date?date.ToString("yyyy-MM-dd"):Convert.ToString(value)??"";
                            // Inline strings preserve leading zeros and never execute user text as a formula.
                            cell.Add(new XAttribute("t","inlineStr"),new XElement(S+"is",new XElement(S+"t",new XAttribute(XNamespace.Xml+"space","preserve"),text)));
                        }
                        row.Add(cell);
                    }
                    rows.Add(row);
                }
                Add(1,["IESTP SAN MARCOS · "+title]);Add(2,["Generado: "+DateTime.Now.ToString("g")]);Add(3,table.Columns.Cast<DataColumn>().Select(c=>(object)c.ColumnName));
                int index=4;foreach(DataRow row in table.Rows)Add(index++,row.ItemArray.Select(v=>v??""));
                Xml("xl/worksheets/sheet1.xml",new XElement(S+"worksheet",new XElement(S+"sheetViews",new XElement(S+"sheetView",new XAttribute("workbookViewId",0),new XElement(S+"pane",new XAttribute("ySplit",3),new XAttribute("topLeftCell","A4"),new XAttribute("activePane","bottomLeft"),new XAttribute("state","frozen")))),new XElement(S+"cols",Enumerable.Range(1,table.Columns.Count).Select(i=>new XElement(S+"col",new XAttribute("min",i),new XAttribute("max",i),new XAttribute("width",24),new XAttribute("customWidth",1)))),rows,new XElement(S+"autoFilter",new XAttribute("ref","A3:"+Column(table.Columns.Count-1)+Math.Max(3,index-1)))));
            }
            File.Move(temp,path,true);
        }
        finally {if(File.Exists(temp))File.Delete(temp);}
    }
}
