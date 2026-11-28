using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace ShumaDesktop;

public class MainForm : Form
{
    CaseData data = new();
    OfficeSettings settings = Storage.LoadSettings();
    string currentPath;
    bool dirty;
    bool loading;

    readonly TabControl tabs = new() { Dock = DockStyle.Fill, RightToLeftLayout = true };
    readonly ToolStrip bar = new() { GripStyle = ToolStripGripStyle.Hidden, Padding = new Padding(6), ImageScalingSize = new Size(20, 20) };
    readonly List<Action> recalcs = new();

    static readonly string[] PartTypes = { "", "מקורי", "תואם", "משומש", "חליפי" };
    static readonly string[] VatModes = { "כולל מע\"מ", "לא כולל מע\"מ" };
    static readonly Font UiFont = new("Segoe UI", 10f);
    static readonly Font BoldFont = new("Segoe UI", 10f, FontStyle.Bold);

    public MainForm()
    {
        Text = "חוות דעת שמאי";
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = true;
        Font = UiFont;
        MinimumSize = new Size(980, 680);
        Size = new Size(1180, 820);
        StartPosition = FormStartPosition.CenterScreen;

        Button(bar, "חדש", (_, _) => NewCase());
        Button(bar, "פתח…", (_, _) => OpenDialog());
        Button(bar, "שמור", (_, _) => Save(false));
        Button(bar, "שמור בשם…", (_, _) => Save(true));
        bar.Items.Add(new ToolStripSeparator());
        var pdf = Button(bar, "הפק PDF", (_, _) => MakePdf());
        pdf.Font = BoldFont; pdf.ForeColor = Color.DarkBlue;

        Controls.Add(tabs);
        Controls.Add(bar);
        if (string.IsNullOrWhiteSpace(data.Notes)) data.Notes = settings.DefaultNotes;
        BuildUi();
        FormClosing += (_, e) => { if (!ConfirmDiscard()) e.Cancel = true; };
    }

    static ToolStripButton Button(ToolStrip s, string text, EventHandler click)
    {
        var b = new ToolStripButton(text) { DisplayStyle = ToolStripItemDisplayStyle.Text, Padding = new Padding(8, 2, 8, 2) };
        b.Click += click; s.Items.Add(b); return b;
    }

    // ================= case lifecycle =================

    void NewCase()
    {
        if (!ConfirmDiscard()) return;
        data = new CaseData { Notes = settings.DefaultNotes };
        currentPath = null; BuildUi(); dirty = false; UpdateTitle();
    }

    void OpenDialog()
    {
        if (!ConfirmDiscard()) return;
        using var d = new OpenFileDialog { Filter = "תיק שמאות (*.shuma)|*.shuma|כל הקבצים|*.*" };
        if (d.ShowDialog(this) == DialogResult.OK) OpenCase(d.FileName);
    }

    public void OpenCase(string path)
    {
        try
        {
            data = Storage.LoadCase(path);
            if (data.Foci.Count == 0) data.Foci.Add(new FocusData());
            currentPath = path; BuildUi(); dirty = false; UpdateTitle();
        }
        catch (Exception ex) { MessageBox.Show(this, "לא ניתן לפתוח את הקובץ:\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error, MessageBoxDefaultButton.Button1, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign); }
    }

    bool Save(bool asNew)
    {
        Validate();
        if (asNew || currentPath == null)
        {
            using var d = new SaveFileDialog { Filter = "תיק שמאות (*.shuma)|*.shuma", FileName = SafeName($"{data.DocTitle} {data.OpinionNo} {data.LicenseNo}") };
            if (d.ShowDialog(this) != DialogResult.OK) return false;
            currentPath = d.FileName;
        }
        try { Storage.SaveCase(data, currentPath); dirty = false; UpdateTitle(); return true; }
        catch (Exception ex) { Msg("השמירה נכשלה:\n" + ex.Message, MessageBoxIcon.Error); return false; }
    }

    bool ConfirmDiscard()
    {
        if (!dirty) return true;
        var r = MessageBox.Show(this, "יש שינויים שלא נשמרו. לשמור אותם?", Text, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question,
                                MessageBoxDefaultButton.Button1, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
        if (r == DialogResult.Cancel) return false;
        if (r == DialogResult.Yes) return Save(false);
        return true;
    }

    void MakePdf()
    {
        Validate();
        using var d = new SaveFileDialog { Filter = "PDF (*.pdf)|*.pdf", FileName = SafeName($"{data.DocTitle} {data.OpinionNo} {data.LicenseNo}") + ".pdf" };
        if (d.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            Cursor = Cursors.WaitCursor;
            PdfBuilder.Generate(data, settings, d.FileName);
            Process.Start(new ProcessStartInfo(d.FileName) { UseShellExecute = true });
        }
        catch (Exception ex) { Msg("הפקת ה-PDF נכשלה:\n" + ex.Message, MessageBoxIcon.Error); }
        finally { Cursor = Cursors.Default; }
    }

    static string SafeName(string s)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '-');
        s = s.Trim(); return s.Length == 0 ? "חוות דעת" : s;
    }

    void Msg(string text, MessageBoxIcon icon = MessageBoxIcon.Information) =>
        MessageBox.Show(this, text, Text, MessageBoxButtons.OK, icon, MessageBoxDefaultButton.Button1, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);

    void UpdateTitle() =>
        Text = "חוות דעת שמאי" + (currentPath != null ? " - " + Path.GetFileNameWithoutExtension(currentPath) : "") + (dirty ? " *" : "");

    void Changed()
    {
        if (loading) return;
        foreach (var r in recalcs) r();
        if (!dirty) { dirty = true; UpdateTitle(); }
    }

    // ================= UI =================

    void BuildUi()
    {
        loading = true;
        recalcs.Clear();
        tabs.SuspendLayout();
        foreach (TabPage p in tabs.TabPages) p.Dispose();
        tabs.TabPages.Clear();
        tabs.TabPages.Add(DetailsTab());
        tabs.TabPages.Add(FociTab());
        tabs.TabPages.Add(SummaryTab());
        tabs.TabPages.Add(FeeTab());
        tabs.TabPages.Add(PhotosTab());
        tabs.TabPages.Add(SettingsTab());
        tabs.ResumeLayout();
        loading = false;
        foreach (var r in recalcs) r();
    }

    static TabPage Page(string title) => new(title) { Padding = new Padding(10), AutoScroll = true, RightToLeft = RightToLeft.Yes };

    static TableLayoutPanel Grid(int cols)
    {
        var t = new TableLayoutPanel { ColumnCount = cols, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new Size(1000, 0), MaximumSize = new Size(1000, 0), Padding = new Padding(0, 0, 0, 8) };
        for (int i = 0; i < cols; i++)
            t.ColumnStyles.Add(i % 2 == 0 ? new ColumnStyle(SizeType.Absolute, 150) : new ColumnStyle(SizeType.Percent, 100f / (cols / 2)));
        return t;
    }

    static Label Lbl(string text, bool bold = false) =>
        new() { Text = text, AutoSize = true, Anchor = AnchorStyles.Right | AnchorStyles.Left, Padding = new Padding(0, 6, 0, 0), Font = bold ? BoldFont : UiFont };

    static Label Section(string text) =>
        new() { Text = text, AutoSize = true, Dock = DockStyle.Top, Font = new Font("Segoe UI", 11.5f, FontStyle.Bold), ForeColor = Color.FromArgb(31, 59, 115), Padding = new Padding(0, 10, 0, 4) };

    TextBox Str(Func<string> get, Action<string> set, bool multiline = false, int height = 70)
    {
        var tb = new TextBox { Text = get() ?? "", Dock = DockStyle.Fill, Multiline = multiline, ScrollBars = multiline ? ScrollBars.Vertical : ScrollBars.None, Margin = new Padding(3) };
        if (multiline) tb.Height = height;
        tb.TextChanged += (_, _) => { set(tb.Text); Changed(); };
        return tb;
    }

    TextBox Dec(Func<decimal?> get, Action<decimal?> set)
    {
        var tb = new TextBox { Text = Num.Plain(get()), Dock = DockStyle.Fill, Margin = new Padding(3), RightToLeft = RightToLeft.No, TextAlign = HorizontalAlignment.Right };
        tb.TextChanged += (_, _) => { set(Num.Parse(tb.Text)); tb.BackColor = tb.Text.Trim().Length > 0 && Num.Parse(tb.Text) == null ? Color.MistyRose : SystemColors.Window; Changed(); };
        return tb;
    }

    ComboBox Combo(string[] items, Func<string> get, Action<string> set, bool editable = true)
    {
        var cb = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = editable ? ComboBoxStyle.DropDown : ComboBoxStyle.DropDownList, Margin = new Padding(3) };
        cb.Items.AddRange(items);
        var v = get() ?? "";
        if (editable) cb.Text = v; else cb.SelectedItem = items.Contains(v) ? v : items[0];
        cb.TextChanged += (_, _) => { set(cb.Text); Changed(); };
        cb.SelectedIndexChanged += (_, _) => { set(cb.Text); Changed(); };
        return cb;
    }

    ComboBox VatCombo(Func<bool> get, Action<bool> set)
    {
        var cb = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(3) };
        cb.Items.AddRange(VatModes);
        cb.SelectedIndex = get() ? 0 : 1;
        cb.SelectedIndexChanged += (_, _) => { set(cb.SelectedIndex == 0); Changed(); };
        return cb;
    }

    Label Out(Func<string> value)
    {
        var l = new Label { AutoSize = true, Font = BoldFont, ForeColor = Color.FromArgb(0, 0, 115), Anchor = AnchorStyles.Right | AnchorStyles.Left, Padding = new Padding(0, 6, 0, 0) };
        recalcs.Add(() => l.Text = value());
        return l;
    }

    static void Add(TableLayoutPanel t, params Control[] cs) { foreach (var c in cs) t.Controls.Add(c); }

    static void FitWidth(FlowLayoutPanel root)
    {
        int w = Math.Max(600, root.ClientSize.Width - 30);
        foreach (Control c in root.Controls)
        {
            if (c is TableLayoutPanel t) { t.MinimumSize = new Size(w, 0); t.MaximumSize = new Size(w, 0); }
            else if (c is TextBox || c is DataGridView) c.Width = w;
        }
    }

    // ---------- tab: details ----------
    TabPage DetailsTab()
    {
        var page = Page("פרטים");
        var root = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };

        var head = Grid(4);
        Add(head, Lbl("כותרת המסמך", true), Combo(new[] { "חוות דעת", "אומדן הנזק" }, () => data.DocTitle, v => data.DocTitle = v),
                  Lbl("מספר חוות דעת", true), Str(() => data.OpinionNo, v => data.OpinionNo = v));
        Add(head, Lbl("תאריך"), Str(() => data.DocDate, v => data.DocDate = v), Lbl("תיק"), Str(() => data.FileNo, v => data.FileNo = v));
        Add(head, Lbl("ע.מ."), Str(() => data.VatId, v => data.VatId = v),
                  Lbl("מצב הרכב"), Combo(new[] { "רכב תוקן בפועל", "פיצוי מוסכם", "אובדן להלכה", "אובדן מוחלט", "" }, () => data.RepairStatus, v => data.RepairStatus = v));
        Add(head, Lbl("לכבוד"), Str(() => data.ToName, v => data.ToName = v), Lbl("כתובת"), Str(() => data.ToAddress, v => data.ToAddress = v));
        Add(head, Lbl("טלפון"), Str(() => data.ToPhone, v => data.ToPhone = v), new Label(), new Label());

        var veh = Grid(4);
        void Pair(string l1, Func<string> g1, Action<string> s1, string l2, Func<string> g2, Action<string> s2) =>
            Add(veh, Lbl(l1), Str(g1, s1), Lbl(l2), Str(g2, s2));
        Pair("המזמין/בעל הרכב", () => data.Owner, v => data.Owner = v, "מס' רישוי", () => data.LicenseNo, v => data.LicenseNo = v);
        Pair("תאריך אירוע", () => data.EventDate, v => data.EventDate = v, "מד אוץ", () => data.Odometer, v => data.Odometer = v);
        Pair("תאריך בדיקה", () => data.InspectionDate, v => data.InspectionDate = v, "מס' שילדה", () => data.Vin, v => data.Vin = v);
        Pair("מקום הבדיקה", () => data.InspectionPlace, v => data.InspectionPlace = v, "שנת יצור", () => data.Year, v => data.Year = v);
        Pair("בעלויות", () => data.Ownership, v => data.Ownership = v, "סוג רכב", () => data.VehicleType, v => data.VehicleType = v);
        Pair("תוצר ודגם", () => data.MakeModel, v => data.MakeModel = v, "סוג חוות דעת", () => data.OpinionType, v => data.OpinionType = v);

        var circ = Str(() => data.Circumstances, v => data.Circumstances = v, true, 70);
        circ.Dock = DockStyle.None; circ.Width = 900;

        root.Controls.Add(Section("פרטי המסמך"));
        root.Controls.Add(head);
        root.Controls.Add(Section("פרטי הרכב"));
        root.Controls.Add(veh);
        root.Controls.Add(Section("נסיבות המקרה"));
        root.Controls.Add(circ);
        root.Resize += (_, _) => FitWidth(root);
        page.Controls.Add(root);
        return page;
    }

    // ---------- tab: foci (works + parts per focus) ----------
    TabPage FociTab()
    {
        var page = Page("מוקדים: עבודות וחלקים");
        var split = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        // left: list of foci
        var side = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4 };
        side.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        side.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        side.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        side.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, Font = BoldFont };
        var addFocus = new System.Windows.Forms.Button { Text = "+ הוסף מוקד", Dock = DockStyle.Fill, Height = 34, BackColor = Color.FromArgb(247, 201, 72) };
        var delFocus = new System.Windows.Forms.Button { Text = "− הסר מוקד", Dock = DockStyle.Fill, Height = 30 };
        side.Controls.Add(Lbl("מוקדי נזק", true), 0, 0);
        side.Controls.Add(list, 0, 1);
        side.Controls.Add(addFocus, 0, 2);
        side.Controls.Add(delFocus, 0, 3);

        // right: editor of the selected focus
        var editor = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 6, ColumnCount = 1 };
        editor.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        editor.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        editor.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
        editor.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        editor.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
        editor.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var nameRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, WrapContents = false };
        var nameBox = new TextBox { Width = 260 };
        nameRow.Controls.Add(Lbl("סה\"כ נזק למוקד", true));
        nameRow.Controls.Add(nameBox);
        nameRow.Controls.Add(Lbl("(לדוגמה: הקדמי, האחורי)"));

        var works = new DataGridView();
        var parts = new DataGridView();
        StyleGrid(works); StyleGrid(parts);
        works.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(WorkRow.Description), HeaderText = "תיאור עבודות", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        works.Columns.Add(MoneyColumn(nameof(WorkRow.Cost), "עלות עבודות"));
        parts.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(PartRow.Description), HeaderText = "תיאור החלק", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        var typeCol = new DataGridViewComboBoxColumn { DataPropertyName = nameof(PartRow.Type), HeaderText = "סוג החלק", Width = 120, FlatStyle = FlatStyle.Flat, DisplayStyle = DataGridViewComboBoxDisplayStyle.ComboBox };
        typeCol.Items.AddRange(PartTypes);
        parts.Columns.Add(typeCol);
        parts.Columns.Add(MoneyColumn(nameof(PartRow.Price), "מחיר החלק"));

        var wTotal = new Label { AutoSize = true, Font = BoldFont, Padding = new Padding(0, 4, 0, 4) };
        var pTotal = new Label { AutoSize = true, Font = BoldFont, Padding = new Padding(0, 4, 0, 4) };
        var fTotal = new Label { AutoSize = true, Font = new Font("Segoe UI", 11f, FontStyle.Bold), ForeColor = Color.FromArgb(0, 0, 115), Padding = new Padding(0, 6, 0, 0) };

        editor.Controls.Add(nameRow, 0, 0);
        editor.Controls.Add(Section("פירוט העבודות (שורה חדשה נוספת אוטומטית בסוף הטבלה)"), 0, 1);
        var wPanel = new Panel { Dock = DockStyle.Fill }; wPanel.Controls.Add(works); wPanel.Controls.Add(wTotal); wTotal.Dock = DockStyle.Bottom;
        editor.Controls.Add(wPanel, 0, 2);
        editor.Controls.Add(Section("פירוט החלקים"), 0, 3);
        var pPanel = new Panel { Dock = DockStyle.Fill }; pPanel.Controls.Add(parts); pPanel.Controls.Add(pTotal); pTotal.Dock = DockStyle.Bottom;
        editor.Controls.Add(pPanel, 0, 4);
        editor.Controls.Add(fTotal, 0, 5);

        FocusData cur = null;
        void RefreshList()
        {
            var sel = list.SelectedIndex;
            list.Items.Clear();
            for (int i = 0; i < data.Foci.Count; i++)
                list.Items.Add($"מוקד {i + 1}" + (string.IsNullOrWhiteSpace(data.Foci[i].Name) ? "" : " - " + data.Foci[i].Name));
            list.SelectedIndex = Math.Min(Math.Max(sel, 0), list.Items.Count - 1);
        }
        void Totals()
        {
            if (cur == null) return;
            wTotal.Text = "סה\"כ עבודות: " + Num.Money(cur.WorksTotal) + " ₪";
            pTotal.Text = "סה\"כ חלקים: " + Num.Money(cur.PartsTotal) + " ₪";
            fTotal.Text = "סה\"כ נזק למוקד: " + Num.Money(cur.Total) + " ₪";
        }
        recalcs.Add(Totals);
        void ShowFocus(int i)
        {
            if (i < 0 || i >= data.Foci.Count) return;
            cur = data.Foci[i];
            var was = loading;
            loading = true;
            nameBox.Text = cur.Name ?? "";
            works.DataSource = cur.Works;
            parts.DataSource = cur.Parts;
            loading = was;
            Totals();
        }
        list.SelectedIndexChanged += (_, _) => ShowFocus(list.SelectedIndex);
        nameBox.TextChanged += (_, _) =>
        {
            if (loading || cur == null) return;
            cur.Name = nameBox.Text;
            var i = list.SelectedIndex;
            list.Items[i] = $"מוקד {i + 1}" + (string.IsNullOrWhiteSpace(cur.Name) ? "" : " - " + cur.Name);
            Changed();
        };
        addFocus.Click += (_, _) =>
        {
            data.Foci.Add(new FocusData());
            RefreshList(); list.SelectedIndex = data.Foci.Count - 1; nameBox.Focus(); Changed();
        };
        delFocus.Click += (_, _) =>
        {
            if (data.Foci.Count <= 1) { Msg("חייב להישאר לפחות מוקד אחד."); return; }
            var i = list.SelectedIndex;
            if (MessageBox.Show(this, $"למחוק את {list.Items[i]} עם כל העבודות והחלקים שלו?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign) != DialogResult.Yes) return;
            data.Foci.RemoveAt(i); RefreshList(); Changed();
        };
        foreach (var g in new[] { works, parts })
        {
            g.CellValueChanged += (_, _) => Changed();
            g.UserDeletedRow += (_, _) => Changed();
            g.RowsAdded += (_, _) => Changed();
        }

        split.Controls.Add(side, 0, 0);
        split.Controls.Add(editor, 1, 0);
        page.Controls.Add(split);
        RefreshList();
        ShowFocus(0);
        return page;
    }

    static DataGridViewTextBoxColumn MoneyColumn(string prop, string header) => new()
    {
        DataPropertyName = prop, HeaderText = header, Width = 130,
        DefaultCellStyle = new DataGridViewCellStyle { Format = "#,##0.##", Alignment = DataGridViewContentAlignment.MiddleLeft, NullValue = "" },
        ValueType = typeof(decimal?)
    };

    void StyleGrid(DataGridView g)
    {
        g.Dock = DockStyle.Fill;
        g.AutoGenerateColumns = false;
        g.AllowUserToAddRows = true;
        g.AllowUserToDeleteRows = true;
        g.RowHeadersWidth = 28;
        g.BackgroundColor = Color.White;
        g.RightToLeft = RightToLeft.Yes;
        g.EnableHeadersVisualStyles = false;
        g.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(237, 237, 237);
        g.ColumnHeadersDefaultCellStyle.Font = BoldFont;
        g.EditMode = DataGridViewEditMode.EditOnEnter;
        g.DataError += (_, e) => { e.ThrowException = false; Msg("ערך לא תקין. בעמודות הסכום יש להקליד מספר בלבד."); };
        g.CurrentCellDirtyStateChanged += (_, _) => { if (g.IsCurrentCellDirty && g.CurrentCell is DataGridViewComboBoxCell) g.CommitEdit(DataGridViewDataErrorContexts.Commit); };
    }

    // ---------- tab: summary, raw damage, depreciation, notes ----------
    TabPage SummaryTab()
    {
        var page = Page("סיכום וירידת ערך");
        var root = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };

        var sum = Grid(4);
        Add(sum, Lbl("סה\"כ נזק (כל המוקדים)", true), Out(() => Num.Money(data.DamageTotal) + " ₪"),
                 Lbl("אחוז מע\"מ"), Dec(() => data.VatPercent, v => data.VatPercent = v ?? 0));
        Add(sum, Lbl("סה\"כ"), VatCombo(() => data.TotalIncludesVat, v => data.TotalIncludesVat = v),
                 Lbl("מע\"מ"), Out(() => data.TotalIncludesVat ? Num.Money(data.DamageVat, 2) + " ₪" : "—"));
        Add(sum, Lbl("סה\"כ לתשלום", true), Out(() => Num.Money(data.TotalFinal, data.TotalIncludesVat ? 2 : 0) + " ₪"), new Label(), new Label());

        var raw = Grid(4);
        Add(raw, Lbl("ערך רכב בסיסי (לוי יצחק) ₪"), Dec(() => data.CarValue, v => data.CarValue = v),
                 Lbl("ערך הרכב"), VatCombo(() => data.CarValueIncludesVat, v => data.CarValueIncludesVat = v));
        Add(raw, Lbl("קוד דגם"), Str(() => data.ModelCode, v => data.ModelCode = v),
                 Lbl("נזק גולמי מערך הרכב"), Out(() => data.RawPercent == null ? "—" : Num.Plain(data.RawPercent) + "%"));

        var reason = Str(() => data.DeprReason, v => data.DeprReason = v);
        var depr = new DataGridView { Height = 190 };
        StyleGrid(depr); depr.Dock = DockStyle.None;
        depr.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(DeprRow.Factor), HeaderText = "גורם ירידת הערך", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        depr.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(DeprRow.Percent), HeaderText = "אחוז", Width = 110, ValueType = typeof(decimal?),
            DefaultCellStyle = new DataGridViewCellStyle { Format = "0.##", Alignment = DataGridViewContentAlignment.MiddleCenter, NullValue = "" }
        });
        depr.DataSource = data.Depreciation;
        depr.CellValueChanged += (_, _) => Changed(); depr.UserDeletedRow += (_, _) => Changed();

        var dv = Grid(4);
        Add(dv, Lbl("סה\"כ אחוז ירידת ערך"), Out(() => Num.Plain(data.DeprPercentTotal) + "%"), new Label(), new Label());
        Add(dv, Lbl("ערך הרכב משוערך ליום האירוע ₪"), Dec(() => data.CarValueEvent, v => data.CarValueEvent = v),
                Lbl("ערך משוערך"), VatCombo(() => data.CarValueEventIncludesVat, v => data.CarValueEventIncludesVat = v));
        Add(dv, Lbl("סה\"כ ירידת ערך", true), Out(() => Num.Money(data.DeprTotal) + " ₪"), new Label(), new Label());

        var notes = Str(() => data.Notes, v => data.Notes = v, true, 130);
        var att = Str(() => data.Attachments, v => data.Attachments = v);

        root.Controls.Add(Section("סיכום הנזק"));
        root.Controls.Add(sum);
        root.Controls.Add(Section("תחשיב נזק גולמי"));
        root.Controls.Add(raw);
        root.Controls.Add(Section("ירידת ערך"));
        root.Controls.Add(reason);
        root.Controls.Add(depr);
        root.Controls.Add(dv);
        root.Controls.Add(Section("הערות"));
        root.Controls.Add(notes);
        root.Controls.Add(Section("לוט"));
        root.Controls.Add(att);
        root.Resize += (_, _) => FitWidth(root);
        page.Controls.Add(root);
        return page;
    }

    // ---------- tab: fee invoice ----------
    TabPage FeeTab()
    {
        var page = Page("חשבון שכ\"ט");
        var root = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
        var inc = new CheckBox { Text = "לצרף חשבון שכ\"ט ל-PDF", Checked = data.IncludeFee, AutoSize = true, Font = BoldFont };
        inc.CheckedChanged += (_, _) => { data.IncludeFee = inc.Checked; Changed(); };
        var g = Grid(2); g.MinimumSize = new Size(560, 0); g.MaximumSize = new Size(560, 0);
        Add(g, Lbl("שכר טרחה"), Dec(() => data.FeeMain, v => data.FeeMain = v));
        Add(g, Lbl("צילומים"), Dec(() => data.FeePhotos, v => data.FeePhotos = v));
        Add(g, Lbl("נסיעות"), Dec(() => data.FeeTravel, v => data.FeeTravel = v));
        Add(g, Lbl("שונות"), Dec(() => data.FeeMisc, v => data.FeeMisc = v));
        Add(g, Lbl("עיגול חשבון שכ\"ט"), Dec(() => data.FeeRound, v => data.FeeRound = v));
        Add(g, Lbl("סה\"כ ללא מע\"מ", true), Out(() => Num.Money(data.FeeNet, 2) + " ₪"));
        Add(g, Lbl("אחוז מע\"מ"), Dec(() => data.FeeVatPercent, v => data.FeeVatPercent = v ?? 0));
        Add(g, Lbl("מע\"מ"), Out(() => Num.Money(data.FeeVat, 2) + " ₪"));
        Add(g, Lbl("סה\"כ כולל מע\"מ", true), Out(() => Num.Money(data.FeeGross, 2) + " ₪"));
        root.Controls.Add(inc);
        root.Controls.Add(g);
        page.Controls.Add(root);
        return page;
    }

    // ---------- tab: photos ----------
    TabPage PhotosTab()
    {
        var page = Page("תמונות");
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 300));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var inc = new CheckBox { Text = "לצרף עמוד תמונות ל-PDF", Checked = data.IncludePhotos, AutoSize = true, Font = BoldFont };
        inc.CheckedChanged += (_, _) => { data.IncludePhotos = inc.Checked; Changed(); };
        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top };
        var add = new System.Windows.Forms.Button { Text = "+ הוסף תמונות…", AutoSize = true, BackColor = Color.FromArgb(247, 201, 72) };
        var del = new System.Windows.Forms.Button { Text = "מחק", AutoSize = true };
        var up = new System.Windows.Forms.Button { Text = "▲", AutoSize = true };
        var down = new System.Windows.Forms.Button { Text = "▼", AutoSize = true };
        buttons.Controls.AddRange(new Control[] { inc, add, del, up, down });

        var list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
        var right = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var pic = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(245, 245, 245), BorderStyle = BorderStyle.FixedSingle };
        var capRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top };
        var cap = new TextBox { Width = 500 };
        capRow.Controls.Add(Lbl("תיאור התמונה"));
        capRow.Controls.Add(cap);
        right.Controls.Add(pic, 0, 0);
        right.Controls.Add(capRow, 0, 1);

        void Fill(int sel)
        {
            list.Items.Clear();
            for (int i = 0; i < data.Photos.Count; i++) list.Items.Add($"{i + 1}. " + (string.IsNullOrWhiteSpace(data.Photos[i].Caption) ? "(ללא תיאור)" : data.Photos[i].Caption));
            if (list.Items.Count > 0) list.SelectedIndex = Math.Min(Math.Max(sel, 0), list.Items.Count - 1);
            else { pic.Image?.Dispose(); pic.Image = null; cap.Text = ""; }
        }
        bool capLoading = false;
        list.SelectedIndexChanged += (_, _) =>
        {
            var i = list.SelectedIndex; if (i < 0) return;
            pic.Image?.Dispose();
            try { pic.Image = Image.FromStream(new MemoryStream(Convert.FromBase64String(data.Photos[i].JpegBase64))); } catch { pic.Image = null; }
            capLoading = true; cap.Text = data.Photos[i].Caption ?? ""; capLoading = false;
        };
        cap.TextChanged += (_, _) =>
        {
            var i = list.SelectedIndex; if (capLoading || i < 0) return;
            data.Photos[i].Caption = cap.Text;
            list.Items[i] = $"{i + 1}. " + (string.IsNullOrWhiteSpace(cap.Text) ? "(ללא תיאור)" : cap.Text);
            Changed();
        };
        add.Click += (_, _) =>
        {
            using var d = new OpenFileDialog { Multiselect = true, Filter = "תמונות|*.jpg;*.jpeg;*.png;*.bmp;*.heic;*.webp|כל הקבצים|*.*" };
            if (d.ShowDialog(this) != DialogResult.OK) return;
            Cursor = Cursors.WaitCursor;
            foreach (var f in d.FileNames)
            {
                try { data.Photos.Add(new PhotoData { JpegBase64 = Convert.ToBase64String(ImageTools.ToJpeg(f, 1600, 85)) }); }
                catch (Exception ex) { Msg($"לא ניתן לפתוח את {Path.GetFileName(f)}:\n{ex.Message}", MessageBoxIcon.Warning); }
            }
            Cursor = Cursors.Default;
            Fill(data.Photos.Count - 1); Changed();
        };
        del.Click += (_, _) => { var i = list.SelectedIndex; if (i < 0) return; data.Photos.RemoveAt(i); Fill(i); Changed(); };
        up.Click += (_, _) => { var i = list.SelectedIndex; if (i <= 0) return; (data.Photos[i - 1], data.Photos[i]) = (data.Photos[i], data.Photos[i - 1]); Fill(i - 1); Changed(); };
        down.Click += (_, _) => { var i = list.SelectedIndex; if (i < 0 || i >= data.Photos.Count - 1) return; (data.Photos[i + 1], data.Photos[i]) = (data.Photos[i], data.Photos[i + 1]); Fill(i + 1); Changed(); };

        layout.Controls.Add(buttons, 0, 0); layout.SetColumnSpan(buttons, 2);
        layout.Controls.Add(list, 0, 1);
        layout.Controls.Add(right, 1, 1);
        page.Controls.Add(layout);
        Fill(0);
        return page;
    }

    // ---------- tab: office / appraiser settings ----------
    TabPage SettingsTab()
    {
        var page = Page("הגדרות משרד ושמאי");
        var root = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
        var s = settings;
        TextBox T(Func<string> g, Action<string> st, bool ml = false, int h = 70)
        {
            var tb = new TextBox { Text = g() ?? "", Dock = DockStyle.Fill, Multiline = ml, ScrollBars = ml ? ScrollBars.Vertical : ScrollBars.None, Margin = new Padding(3) };
            if (ml) tb.Height = h;
            tb.TextChanged += (_, _) => st(tb.Text);
            return tb;
        }
        var g1 = Grid(2); g1.MinimumSize = new Size(860, 0); g1.MaximumSize = new Size(860, 0);
        Add(g1, Lbl("שורת כותרת"), T(() => s.OfficeTitle, v => s.OfficeTitle = v));
        Add(g1, Lbl("כתובת"), T(() => s.Address, v => s.Address = v));
        Add(g1, Lbl("נייד"), T(() => s.Mobile, v => s.Mobile = v));
        Add(g1, Lbl("פקס"), T(() => s.Fax, v => s.Fax = v));
        Add(g1, Lbl("דוא\"ל"), T(() => s.Email, v => s.Email = v));
        Add(g1, Lbl("שם השמאי (בחתימה)"), T(() => s.AppraiserName, v => s.AppraiserName = v));
        Add(g1, Lbl("שורה שנייה בחתימה"), T(() => s.AppraiserSubtitle, v => s.AppraiserSubtitle = v));
        Add(g1, Lbl("מספר רישיון שמאי"), T(() => s.LicenseNo, v => s.LicenseNo = v));
        Add(g1, Lbl("הערות ברירת מחדל"), T(() => s.DefaultNotes, v => s.DefaultNotes = v, true, 120));

        var sigRow = new FlowLayoutPanel { AutoSize = true };
        var sigPic = new PictureBox { Width = 260, Height = 90, SizeMode = PictureBoxSizeMode.Zoom, BorderStyle = BorderStyle.FixedSingle, BackColor = Color.White };
        void ShowSig() { sigPic.Image?.Dispose(); sigPic.Image = null; try { if (!string.IsNullOrEmpty(s.SignaturePngBase64)) sigPic.Image = Image.FromStream(new MemoryStream(Convert.FromBase64String(s.SignaturePngBase64))); } catch { } }
        var sigLoad = new System.Windows.Forms.Button { Text = "בחר תמונת חתימה…", AutoSize = true };
        var sigClear = new System.Windows.Forms.Button { Text = "הסר חתימה", AutoSize = true };
        sigLoad.Click += (_, _) =>
        {
            using var d = new OpenFileDialog { Filter = "תמונות|*.png;*.jpg;*.jpeg;*.bmp" };
            if (d.ShowDialog(this) != DialogResult.OK) return;
            try { s.SignaturePngBase64 = Convert.ToBase64String(ImageTools.ToPng(d.FileName, 600)); ShowSig(); }
            catch (Exception ex) { Msg(ex.Message, MessageBoxIcon.Warning); }
        };
        sigClear.Click += (_, _) => { s.SignaturePngBase64 = ""; ShowSig(); };
        sigRow.Controls.AddRange(new Control[] { sigPic, sigLoad, sigClear });
        ShowSig();

        var save = new System.Windows.Forms.Button { Text = "שמור הגדרות", AutoSize = true, BackColor = Color.FromArgb(247, 201, 72), Font = BoldFont };
        save.Click += (_, _) => { try { Storage.SaveSettings(s); Msg("ההגדרות נשמרו. הן יופיעו בכל חוות דעת שתפיק."); } catch (Exception ex) { Msg(ex.Message, MessageBoxIcon.Error); } };

        root.Controls.Add(Section("פרטי המשרד (כותרת המסמך)"));
        root.Controls.Add(g1);
        root.Controls.Add(Section("חתימה"));
        root.Controls.Add(sigRow);
        root.Controls.Add(Lbl("מומלץ: חתימה על דף לבן, צילום או סריקה, ושמירה כקובץ PNG."));
        root.Controls.Add(save);
        page.Controls.Add(root);
        return page;
    }
}

static class ImageTools
{
    public static byte[] ToJpeg(string path, int max, long quality)
    {
        using var src = LoadOriented(path);
        using var bmp = Scale(src, max);
        var enc = ImageCodecInfo.GetImageEncoders().First(e => e.FormatID == ImageFormat.Jpeg.Guid);
        using var ps = new EncoderParameters(1);
        ps.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
        using var ms = new MemoryStream();
        bmp.Save(ms, enc, ps);
        return ms.ToArray();
    }

    public static byte[] ToPng(string path, int max)
    {
        using var src = LoadOriented(path);
        using var bmp = Scale(src, max);
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    static Image LoadOriented(string path)
    {
        var img = Image.FromStream(new MemoryStream(File.ReadAllBytes(path)));
        const int OrientationId = 0x0112;
        if (img.PropertyIdList.Contains(OrientationId))
        {
            var o = img.GetPropertyItem(OrientationId)?.Value?[0] ?? 1;
            var flip = o switch { 3 => RotateFlipType.Rotate180FlipNone, 6 => RotateFlipType.Rotate90FlipNone, 8 => RotateFlipType.Rotate270FlipNone, _ => RotateFlipType.RotateNoneFlipNone };
            if (flip != RotateFlipType.RotateNoneFlipNone) img.RotateFlip(flip);
        }
        return img;
    }

    static Bitmap Scale(Image src, int max)
    {
        double s = Math.Min(1.0, (double)max / Math.Max(src.Width, src.Height));
        int w = Math.Max(1, (int)(src.Width * s)), h = Math.Max(1, (int)(src.Height * s));
        var bmp = new Bitmap(w, h);
        using var g = Graphics.FromImage(bmp);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.Clear(Color.White);
        g.DrawImage(src, 0, 0, w, h);
        return bmp;
    }
}
