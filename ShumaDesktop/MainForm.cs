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

    static readonly string[] PartTypes = { "מקורי", "תואם", "משומש", "חליפי", "" };
    static readonly string[] VatModes = { "כולל מע\"מ", "לא כולל מע\"מ" };
    static readonly Font UiFont = new("Segoe UI", 10f);
    /// <summary>Editable fields are light yellow, like the marked fields in the office template.</summary>
    static readonly Color EditBack = Color.FromArgb(255, 249, 214);
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
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
        try
        {
            using var st = typeof(MainForm).Assembly.GetManifestResourceStream("logo.png");
            if (st != null)
            {
                var logo = new Bitmap(Image.FromStream(st), new Size(34, 33));
                bar.Items.Add(new ToolStripLabel(" חוות דעת שמאי ", logo) { Font = BoldFont, ImageScaling = ToolStripItemImageScaling.None, Alignment = ToolStripItemAlignment.Right });
            }
        }
        catch { }

        Button(bar, "חדש", (_, _) => NewCase());
        Button(bar, "פתח…", (_, _) => OpenDialog());
        Button(bar, "שמור", (_, _) => Save(false));
        Button(bar, "שמור בשם…", (_, _) => Save(true));
        bar.Items.Add(new ToolStripSeparator());
        var addF = Button(bar, "+ הוסף מוקד נזק", (_, _) => AddFocus());
        addF.Font = BoldFont; addF.BackColor = Color.FromArgb(247, 201, 72);
        bar.Items.Add(new ToolStripSeparator());
        var chk = Button(bar, "✔ בדיקה לפני הדפסה", (_, _) => CheckBeforePrint());
        chk.Font = BoldFont; chk.ForeColor = Color.DarkGreen;
        var pdf = Button(bar, "הפק PDF", (_, _) => MakePdf());
        pdf.Font = BoldFont; pdf.ForeColor = Color.DarkBlue;

        Controls.Add(tabs);
        Controls.Add(bar);
        if (string.IsNullOrWhiteSpace(data.Notes)) data.Notes = settings.DefaultNotes;
        if (string.IsNullOrWhiteSpace(data.InspectingAppraiser)) data.InspectingAppraiser = settings.AppraiserName;
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
        data = new CaseData { Notes = settings.DefaultNotes, InspectingAppraiser = settings.AppraiserName };
        currentPath = null; BuildUi(); dirty = false; UpdateTitle();
    }

    void OpenDialog()
    {
        if (!ConfirmDiscard()) return;
        using var d = new OpenFileDialog { Filter = "תיק שמאות (*.shuma)|*.shuma|כל הקבצים|*.*", InitialDirectory = Storage.ArchiveDir(settings) };
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
            using var d = new SaveFileDialog { Filter = "תיק שמאות (*.shuma)|*.shuma", InitialDirectory = currentPath != null ? Path.GetDirectoryName(currentPath) : Storage.ArchiveDir(settings), FileName = SafeName($"{data.DocTitle} {data.OpinionNo} {data.LicenseNo}") };
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
        using var d = new SaveFileDialog { Filter = "PDF (*.pdf)|*.pdf", InitialDirectory = currentPath != null ? Path.GetDirectoryName(currentPath) : Storage.ArchiveDir(settings), FileName = SafeName($"{data.DocTitle} {data.OpinionNo} {data.LicenseNo}") + ".pdf" };
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

    /// <summary>Lists missing or suspicious details, then opens a preview of the report without saving it.</summary>
    void CheckBeforePrint()
    {
        Validate();
        var miss = new List<string>();
        void Need(string v, string name) { if (string.IsNullOrWhiteSpace(v)) miss.Add(name); }
        Need(data.OpinionNo, "מספר חוות דעת");
        Need(data.DocDate, "תאריך");
        Need(data.ToName, "לכבוד");
        Need(data.InsuredName, "שם המבוטח");
        Need(data.Insurer, "חברת ביטוח");
        Need(data.EventDate, "תאריך אירוע");
        Need(data.InspectionDate, "תאריך בדיקה");
        Need(data.LicenseNo, "מספר רישוי");
        Need(data.Manufacturer, "יצרן");
        Need(data.Model, "דגם");
        Need(data.Year, "שנת ייצור");
        Need(data.Odometer, "מד אוץ");
        Need(data.OwnerName, "שם בעל הרכב");
        if (data.Circumstances.Contains("_____")) miss.Add("נסיבות המקרה – לא הושלם החלק הפגוע (_____)");
        for (int i = 0; i < data.Foci.Count; i++)
        {
            var f = data.Foci[i]; var n = $"מוקד {i + 1}";
            if (string.IsNullOrWhiteSpace(f.Name)) miss.Add($"{n} – שם המוקד");
            if (f.Total == 0) miss.Add($"{n} – אין עבודות, צבע או חלקים עם סכום");
            foreach (var w in f.Works) if (!string.IsNullOrWhiteSpace(w.Description) && w.Cost == null) miss.Add($"{n} – עבודה \"{w.Description}\" ללא סכום");
            foreach (var w in f.Paint) if (!string.IsNullOrWhiteSpace(w.Description) && w.Cost == null) miss.Add($"{n} – צבע \"{w.Description}\" ללא סכום");
            foreach (var pr in f.Parts) if (!string.IsNullOrWhiteSpace(pr.Description) && pr.Price == null) miss.Add($"{n} – חלק \"{pr.Description}\" ללא מחיר");
        }
        if (data.ListValue == null) miss.Add("ערך רכב (מחירון)");
        if (data.IncludeDepreciation && data.DeprPercent == null) miss.Add("ירידת ערך – אחוז");
        if (data.IncludeFee && data.FeeMain == null) miss.Add("שכר טרחה");
        if (data.IncludePhotos && data.Photos.Count == 0) miss.Add("תמונות – סומן לצרף אך לא נוספו תמונות");
        if (string.IsNullOrWhiteSpace(settings.AppraiserName)) miss.Add("הגדרות – שם השמאי");
        if (string.IsNullOrWhiteSpace(settings.SignaturePngBase64)) miss.Add("הגדרות – תמונת חתימה");

        string text = miss.Count == 0
            ? "הבדיקה עברה – כל הפרטים העיקריים מולאו.\n\nלפתוח תצוגה מקדימה של הדוח?"
            : "חסרים או לא הושלמו הפרטים הבאים:\n\n• " + string.Join("\n• ", miss.Take(25)) +
              (miss.Count > 25 ? $"\n… ועוד {miss.Count - 25}" : "") + "\n\nלפתוח בכל זאת תצוגה מקדימה של הדוח?";
        var r = MessageBox.Show(this, text, "בדיקה לפני הדפסה", MessageBoxButtons.YesNo,
            miss.Count == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button1, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
        if (r != DialogResult.Yes) return;
        try
        {
            Cursor = Cursors.WaitCursor;
            var tmp = Path.Combine(Path.GetTempPath(), "ShumaDesktop-preview-" + DateTime.Now.ToString("HHmmss") + ".pdf");
            PdfBuilder.Generate(data, settings, tmp);
            Process.Start(new ProcessStartInfo(tmp) { UseShellExecute = true });
        }
        catch (Exception ex) { Msg("התצוגה המקדימה נכשלה:\n" + ex.Message, MessageBoxIcon.Error); }
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
        tabs.TabPages.Add(TextsTab());
        tabs.TabPages.Add(FociTab());
        tabs.TabPages.Add(SummaryTab());
        tabs.TabPages.Add(DepreciationTab());
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
        var tb = new TextBox { Text = get() ?? "", Dock = DockStyle.Fill, Multiline = multiline, ScrollBars = multiline ? ScrollBars.Vertical : ScrollBars.None, Margin = new Padding(3), BackColor = EditBack };
        if (multiline) tb.Height = height;
        tb.TextChanged += (_, _) => { set(tb.Text); Changed(); };
        return tb;
    }

    TextBox Dec(Func<decimal?> get, Action<decimal?> set)
    {
        var tb = new TextBox { Text = Num.Plain(get()), Dock = DockStyle.Fill, Margin = new Padding(3), RightToLeft = RightToLeft.No, TextAlign = HorizontalAlignment.Right, BackColor = EditBack };
        tb.TextChanged += (_, _) => { set(Num.Parse(tb.Text)); tb.BackColor = tb.Text.Trim().Length > 0 && Num.Parse(tb.Text) == null ? Color.MistyRose : EditBack; Changed(); };
        return tb;
    }

    ComboBox Combo(string[] items, Func<string> get, Action<string> set, bool editable = true)
    {
        var cb = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = editable ? ComboBoxStyle.DropDown : ComboBoxStyle.DropDownList, Margin = new Padding(3), BackColor = EditBack };
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

    // ---------- helpers for form sections ----------
    FlowLayoutPanel Root()
    {
        var root = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
        root.Resize += (_, _) => FitWidth(root);
        return root;
    }

    void Pairs(TableLayoutPanel g, params (string label, Func<string> get, Action<string> set)[] items)
    {
        foreach (var (label, get, set) in items) Add(g, Lbl(label), Str(get, set));
        if (items.Length % 2 == 1) Add(g, new Label(), new Label());
    }

    // ---------- tab: details ----------
    TabPage DetailsTab()
    {
        var page = Page("פרטים");
        var root = Root();
        var d = data;

        var doc = Grid(4);
        Add(doc, Lbl("כותרת", true), Combo(new[] { "חוות דעת", "אומדן הנזק" }, () => d.DocTitle, v => d.DocTitle = v),
                 Lbl("מספר חוות דעת", true), Str(() => d.OpinionNo, v => d.OpinionNo = v));
        Add(doc, Lbl("תאריך"), Str(() => d.DocDate, v => d.DocDate = v),
                 Lbl("סטטוס"), Combo(new[] { "", "פיצוי מוסכם", "אובדן להלכה", "אובדן מוחלט", "רכב תוקן בפועל" }, () => d.Status, v => d.Status = v));
        Pairs(doc, ("לכבוד", () => d.ToName, v => d.ToName = v), ("כתובת", () => d.ToAddress, v => d.ToAddress = v), ("מיקוד", () => d.ToZip, v => d.ToZip = v));

        var ins = Grid(4);
        Pairs(ins,
            ("שם מבוטח", () => d.InsuredName, v => d.InsuredName = v), ("מבטח", () => d.Insurer, v => d.Insurer = v),
            ("שמאי בודק", () => d.InspectingAppraiser, v => d.InspectingAppraiser = v), ("תאריך אירוע", () => d.EventDate, v => d.EventDate = v),
            ("תאריך בדיקה", () => d.InspectionDate, v => d.InspectionDate = v), ("מקום בדיקה", () => d.InspectionPlace, v => d.InspectionPlace = v),
            ("מוסך", () => d.Garage, v => d.Garage = v), ("סוג תיק", () => d.CaseType, v => d.CaseType = v),
            ("תאריך רישוי", () => d.LicenseDate, v => d.LicenseDate = v), ("מספר פוליסה", () => d.PolicyNo, v => d.PolicyNo = v),
            ("מספר תביעה", () => d.ClaimNo, v => d.ClaimNo = v), ("ת.ז. מבוטח", () => d.InsuredId, v => d.InsuredId = v),
            ("טלפון מבוטח", () => d.InsuredPhone, v => d.InsuredPhone = v), ("הסניף", () => d.Branch, v => d.Branch = v));

        var veh = Grid(4);
        Pairs(veh,
            ("מספר רישוי", () => d.LicenseNo, v => d.LicenseNo = v), ("יצרן", () => d.Manufacturer, v => d.Manufacturer = v),
            ("דגם", () => d.Model, v => d.Model = v), ("שנת יצור", () => d.Year, v => d.Year = v),
            ("קוד דגם", () => d.ModelCode, v => d.ModelCode = v), ("מד אוץ", () => d.Odometer, v => d.Odometer = v),
            ("מספר שלדה", () => d.Vin, v => d.Vin = v), ("מקוריות רכב", () => d.Originality, v => d.Originality = v),
            ("סוג רכב", () => d.VehicleType, v => d.VehicleType = v), ("מספר בעלים", () => d.OwnersCount, v => d.OwnersCount = v),
            ("קוד משרד התחבורה", () => d.MotCode, v => d.MotCode = v));

        var own = Grid(4);
        var ownName = Str(() => d.OwnerName, v => d.OwnerName = v);
        var ownPhone = Str(() => d.OwnerPhone, v => d.OwnerPhone = v);
        var ownAddr = Str(() => d.OwnerAddress, v => d.OwnerAddress = v);
        var ownZip = Str(() => d.OwnerZip, v => d.OwnerZip = v);
        Add(own, Lbl("שם"), ownName, Lbl("מספר טלפון"), ownPhone, Lbl("כתובת"), ownAddr, Lbl("מיקוד"), ownZip);
        var copy = new System.Windows.Forms.Button { Text = "העתק פרטים מהמבוטח ומ\"לכבוד\"", AutoSize = true };
        copy.Click += (_, _) =>
        {
            if (ownName.Text.Length == 0) ownName.Text = d.InsuredName;
            if (ownPhone.Text.Length == 0) ownPhone.Text = d.InsuredPhone;
            if (ownAddr.Text.Length == 0) ownAddr.Text = d.ToAddress;
            if (ownZip.Text.Length == 0) ownZip.Text = d.ToZip;
        };

        root.Controls.Add(Section("פרטי המסמך"));
        root.Controls.Add(doc);
        root.Controls.Add(Section("פרטים ביטוחיים"));
        root.Controls.Add(ins);
        root.Controls.Add(Section("פרטי רכב"));
        root.Controls.Add(veh);
        root.Controls.Add(Section("פרטי בעל הרכב"));
        root.Controls.Add(own);
        root.Controls.Add(copy);
        page.Controls.Add(root);
        return page;
    }

    // ---------- tab: texts ----------
    static readonly Font TextFont = new("Segoe UI", 12f);

    static readonly string[] IntroPhrases =
    {
        "המבוטח פנה למשרדינו וביקש לסכם את התביעה על בסיס גלובלי (נזק מוסכם).",
        "לאור האמור לעיל, חוות דעתנו סוכמה על בסיס ערך נזק מוסכם כולל מע\"מ.",
        "כל מחירי העבודות והחלקים הכלולים באומדן הנזק בחוות דעתנו נכונים ליום בדיקתנו.",
        "הרכב תוקן בפועל במוסך המצוין לעיל, וחוות דעתנו נערכה לפי החשבוניות שהוצגו בפנינו.",
        "לאור היקף הנזק, הרכב הוכרז כאובדן להלכה.",
        "לאור היקף הנזק, הרכב הוכרז כאובדן מוחלט.",
    };
    static readonly string[] CircPhrases =
    {
        "הרכב המצוין לעיל נפגע בחלקו הקדמי.",
        "הרכב המצוין לעיל נפגע בחלקו האחורי.",
        "הרכב המצוין לעיל נפגע בחלקו הימני.",
        "הרכב המצוין לעיל נפגע בחלקו השמאלי.",
        "ראה תיאור האירוע: כתוצאה מפגיעה בו של צד ג'.",
        "ראה תיאור האירוע: כתוצאה מפגיעה ברכב אחר.",
        "ראה תיאור האירוע: כתוצאה מפגיעה בעצם דומם.",
        "ראה תיאור האירוע: הרכב נפגע בעת שעמד בחניה.",
    };
    static readonly string[] DamagePhrases =
    {
        "בהתאם לבקשתך/כם בדקנו את המכונית הנדונה כפי שהוצגה בפנינו.",
        "החלקים המפורטים בחלקים ניזוקו, יצאו מכלל שימוש.",
        "החלקים הניזוקים ניתנים לתיקון ולא נדרשת החלפתם.",
        "הנזקים תואמים את נסיבות האירוע כפי שתוארו בפנינו.",
        "לא נמצאו נזקים קודמים באזור הפגיעה.",
    };

    /// <summary>A section of the texts tab: title, a short caption, a large full-width text box and a list of ready phrases.</summary>
    void TextBlock(FlowLayoutPanel root, string title, string caption, Func<string> get, Action<string> set, string[] phrases, int height)
    {
        root.Controls.Add(Section(title));
        root.Controls.Add(new Label { Text = caption, AutoSize = true, ForeColor = Color.DimGray, Padding = new Padding(0, 0, 0, 4) });
        var tb = Str(get, set, true, height);
        tb.Font = TextFont; tb.WordWrap = true; tb.AcceptsReturn = true; tb.Dock = DockStyle.None;
        var bar = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 0) };
        var pick = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 560, DropDownWidth = 760 };
        pick.Items.AddRange(phrases);
        var ins = new System.Windows.Forms.Button { Text = "+ הוסף נוסח", AutoSize = true, BackColor = Color.FromArgb(247, 201, 72) };
        ins.Click += (_, _) =>
        {
            if (pick.SelectedItem is not string ph) { pick.DroppedDown = true; return; }
            var t = tb.Text; int at = tb.SelectionStart;
            var sep = at > 0 && !char.IsWhiteSpace(t[at - 1]) ? " " : "";
            tb.Text = t.Insert(at, sep + ph); tb.SelectionStart = at + sep.Length + ph.Length; tb.Focus();
        };
        var clr = new System.Windows.Forms.Button { Text = "נקה", AutoSize = true };
        clr.Click += (_, _) => { if (tb.Text.Length == 0) return; if (MessageBox.Show(this, $"למחוק את כל הטקסט ב\"{title}\"?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign) == DialogResult.Yes) tb.Clear(); };
        bar.Controls.Add(new Label { Text = "נוסחים מוכנים:", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
        bar.Controls.Add(pick); bar.Controls.Add(ins); bar.Controls.Add(clr);
        root.Controls.Add(bar);
        root.Controls.Add(tb);
    }

    TabPage TextsTab()
    {
        var page = Page("מבוא ונסיבות");
        var root = Root();
        TextBlock(root, "מבוא", "על איזה בסיס סוכמה חוות הדעת (נזק מוסכם, תיקון בפועל, אובדן) ולאיזה יום נכונים המחירים.",
                  () => data.Intro, v => data.Intro = v, IntroPhrases, 170);
        TextBlock(root, "נסיבות המקרה", "באיזה חלק נפגע הרכב ואיך קרה האירוע. השלם את המקום המסומן _____.",
                  () => data.Circumstances, v => data.Circumstances = v, CircPhrases, 150);
        TextBlock(root, "תיאור הנזקים", "מה נבדק ומה מצב החלקים הניזוקים. פירוט העבודות והחלקים נמצא בלשונית \"מוקדי נזק\".",
                  () => data.DamageDescription, v => data.DamageDescription = v, DamagePhrases, 150);
        page.Controls.Add(root);
        return page;
    }

    // ---------- tab: damage foci ----------
    TabControl fociTabs;
    TabPage fociPage;

    TabPage FociTab()
    {
        var page = Page("מוקדי נזק"); fociPage = page;
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(0, 0, 0, 6) };
        var add = new System.Windows.Forms.Button { Text = "+ הוסף מוקד נזק", AutoSize = true, Font = BoldFont, BackColor = Color.FromArgb(247, 201, 72), Padding = new Padding(10, 4, 10, 4) };
        var del = new System.Windows.Forms.Button { Text = "− הסר את המוקד הפתוח", AutoSize = true, Padding = new Padding(6, 4, 6, 4) };
        top.Controls.Add(add); top.Controls.Add(del);
        top.Controls.Add(Lbl("לכל מוקד יש לשונית משלו, עם עבודות, צבע וחלקי חילוף."));
        fociTabs = new TabControl { Dock = DockStyle.Fill, RightToLeftLayout = true };
        add.Click += (_, _) => AddFocus();
        del.Click += (_, _) =>
        {
            if (data.Foci.Count <= 1) { Msg("חייב להישאר לפחות מוקד אחד."); return; }
            var i = fociTabs.SelectedIndex;
            if (MessageBox.Show(this, $"למחוק את \"{fociTabs.TabPages[i].Text}\" עם כל העבודות והחלקים שלו?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign) != DialogResult.Yes) return;
            data.Foci.RemoveAt(i); RebuildFoci(Math.Max(0, i - 1)); Changed();
        };
        page.Controls.Add(fociTabs);
        page.Controls.Add(top);
        RebuildFoci(0);
        return page;
    }

    void AddFocus()
    {
        data.Foci.Add(new FocusData());
        tabs.SelectedTab = fociPage;
        RebuildFoci(data.Foci.Count - 1);
        Changed();
    }

    static string FocusTitle(FocusData f, int i) => $"מוקד {i + 1}" + (string.IsNullOrWhiteSpace(f.Name) ? "" : " - " + f.Name);

    void RebuildFoci(int select)
    {
        var was = loading; loading = true;
        foreach (TabPage p in fociTabs.TabPages) p.Dispose();
        fociTabs.TabPages.Clear();
        recalcs.RemoveAll(r => r.Target is FocusTotals);
        for (int i = 0; i < data.Foci.Count; i++) fociTabs.TabPages.Add(FocusPage(data.Foci[i], i));
        fociTabs.SelectedIndex = Math.Min(select, fociTabs.TabCount - 1);
        loading = was;
        foreach (var r in recalcs) r();
    }

    class FocusTotals { public Action Run; public void Invoke() => Run(); }

    TabPage FocusPage(FocusData f, int index)
    {
        var page = new TabPage(FocusTitle(f, index)) { Padding = new Padding(6), RightToLeft = RightToLeft.Yes };
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 8 };
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 30));
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 22));
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        t.RowStyles.Add(new RowStyle(SizeType.Percent, 48));
        t.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var nameRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        var name = new TextBox { Width = 320, Text = f.Name ?? "", Font = BoldFont, BackColor = EditBack };
        nameRow.Controls.Add(Lbl("שם המוקד", true));
        nameRow.Controls.Add(name);
        nameRow.Controls.Add(Lbl("לדוגמה: נזק בחזית צד ימין, הקדמי, האחורי"));
        name.TextChanged += (_, _) => { if (loading) return; f.Name = name.Text; page.Text = FocusTitle(f, index); Changed(); };

        var works = CostGrid(f.Works, "תיאור עבודה", "עלות עבודה");
        var paint = CostGrid(f.Paint, "עבודות צבע", "עלות");
        var parts = new DataGridView(); StyleGrid(parts);
        parts.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(PartRow.Description), HeaderText = "תיאור החלק", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 60 });
        parts.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(PartRow.CatalogNo), HeaderText = "מק\"ט", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 30 });
        var typeCol = new DataGridViewComboBoxColumn { DataPropertyName = nameof(PartRow.Type), HeaderText = "סוג החלק", Width = 110, FlatStyle = FlatStyle.Flat, DisplayStyle = DataGridViewComboBoxDisplayStyle.ComboBox };
        typeCol.Items.AddRange(PartTypes);
        parts.Columns.Add(typeCol);
        parts.Columns.Add(MoneyColumn(nameof(PartRow.Price), "סה\"כ מחיר"));
        parts.DataSource = f.Parts;
        HookGrid(parts);

        var wTot = TotalLabel(); var pnTot = TotalLabel(); var pTot = TotalLabel();
        var fTot = new Label { AutoSize = true, Font = new Font("Segoe UI", 11f, FontStyle.Bold), ForeColor = Color.FromArgb(0, 0, 115), Padding = new Padding(0, 6, 0, 0) };
        var holder = new FocusTotals();
        holder.Run = () =>
        {
            wTot.Text = "סה\"כ עבודות ועבודות נלוות: " + Num.Shekel(f.WorksTotal);
            pnTot.Text = "סה\"כ עבודות צבע: " + Num.Shekel(f.PaintTotal);
            pTot.Text = "מחיר חלקי חילוף: " + Num.Shekel(f.PartsTotal);
            fTot.Text = $"סה\"כ נזק למוקד: {Num.Shekel(f.Total)}    מע\"מ: {Num.Shekel(data.FocusVat(f))}    כולל מע\"מ: {Num.Shekel(f.Total + data.FocusVat(f))}";
        };
        recalcs.Add(holder.Invoke);

        t.Controls.Add(nameRow, 0, 0);
        t.Controls.Add(GridTitle("עבודות", wTot), 0, 1);
        t.Controls.Add(works, 0, 2);
        t.Controls.Add(GridTitle("צבע", pnTot), 0, 3);
        t.Controls.Add(paint, 0, 4);
        t.Controls.Add(GridTitle("חלקי חילוף", pTot), 0, 5);
        t.Controls.Add(parts, 0, 6);
        t.Controls.Add(fTot, 0, 7);
        page.Controls.Add(t);
        return page;
    }

    static Label TotalLabel() => new() { AutoSize = true, Font = BoldFont, Padding = new Padding(20, 8, 0, 0) };

    static Control GridTitle(string text, Label total)
    {
        var p = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        p.Controls.Add(new Label { Text = text, AutoSize = true, Font = new Font("Segoe UI", 11.5f, FontStyle.Bold), ForeColor = Color.FromArgb(31, 59, 115), Padding = new Padding(0, 6, 0, 2) });
        p.Controls.Add(total);
        return p;
    }

    DataGridView CostGrid(BindingList<CostRow> rows, string descHeader, string costHeader)
    {
        var g = new DataGridView(); StyleGrid(g);
        g.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(CostRow.Description), HeaderText = descHeader, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        g.Columns.Add(MoneyColumn(nameof(CostRow.Cost), costHeader));
        g.DataSource = rows;
        HookGrid(g);
        return g;
    }

    void HookGrid(DataGridView g)
    {
        g.CellValueChanged += (_, _) => Changed();
        g.UserDeletedRow += (_, _) => Changed();
        g.CellEndEdit += (_, _) => Changed();
    }

    static DataGridViewTextBoxColumn MoneyColumn(string prop, string header) => new()
    {
        DataPropertyName = prop, HeaderText = header, Width = 130,
        DefaultCellStyle = new DataGridViewCellStyle { Format = "#,##0.00", Alignment = DataGridViewContentAlignment.MiddleLeft, NullValue = "" },
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
        g.DefaultCellStyle.BackColor = EditBack;
        g.RightToLeft = RightToLeft.Yes;
        g.EnableHeadersVisualStyles = false;
        g.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(237, 237, 237);
        g.ColumnHeadersDefaultCellStyle.Font = BoldFont;
        g.EditMode = DataGridViewEditMode.EditOnEnter;
        g.DataError += (_, e) => { e.ThrowException = false; Msg("ערך לא תקין. בעמודות הסכום והאחוז יש להקליד מספר בלבד."); };
        g.CurrentCellDirtyStateChanged += (_, _) => { if (g.IsCurrentCellDirty && g.CurrentCell is DataGridViewComboBoxCell) g.CommitEdit(DataGridViewDataErrorContexts.Commit); };
    }

    // ---------- tab: summary and vehicle value ----------
    TabPage SummaryTab()
    {
        var page = Page("סיכום ושווי רכב");
        var root = Root();

        var sum = Grid(4);
        Add(sum, Lbl("סה\"כ הוצאות", true), Out(() => Num.Shekel(data.Expenses)),
                 Lbl("אחוז מע\"מ"), Dec(() => data.VatPercent, v => data.VatPercent = v ?? 0));
        Add(sum, Lbl("מע\"מ"), Out(() => Num.Shekel(data.ExpensesVat)),
                 Lbl("סה\"כ כולל מע\"מ", true), Out(() => Num.Shekel(data.ExpensesGross)));
        Add(sum, Lbl("בדוח להציג"), VatCombo(() => data.ShowVat, v => data.ShowVat = v), new Label(), new Label());

        var val = Grid(4);
        Add(val, Lbl("ערך רכב מחירון ₪", true), Dec(() => data.ListValue, v => data.ListValue = v),
                 Lbl("סה\"כ שווי ערך רכב בפועל", true), Out(() => Num.Shekel(data.ActualValue)));

        const int MaxAdj = 5;
        if (data.ValueAdjustments.Count == 0) data.ValueAdjustments.Add(new ValueRow());
        var adj = new DataGridView();
        StyleGrid(adj); adj.Dock = DockStyle.None;
        adj.AllowUserToAddRows = false; adj.AllowUserToDeleteRows = false;
        adj.ScrollBars = ScrollBars.None;
        adj.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(ValueRow.Subject), HeaderText = "נושא (למשל: הפחתה בגין מס' ק\"מ)", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 50 });
        var sign = new DataGridViewComboBoxColumn { DataPropertyName = nameof(ValueRow.Sign), HeaderText = "-/+", Width = 55, FlatStyle = FlatStyle.Flat };
        sign.Items.AddRange("-", "+");
        adj.Columns.Add(sign);
        adj.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(ValueRow.Percent), HeaderText = "באחוז", Width = 80, ValueType = typeof(decimal?),
            DefaultCellStyle = new DataGridViewCellStyle { Format = "0.00", Alignment = DataGridViewContentAlignment.MiddleCenter, NullValue = "" }
        });
        adj.Columns.Add(MoneyColumn(nameof(ValueRow.Amount), "סכום (ריק = לפי האחוז)"));
        adj.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(ValueRow.Notes), HeaderText = "הערות", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 30 });
        adj.DataSource = data.ValueAdjustments;
        HookGrid(adj);

        var adjBar = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        var addRow = new System.Windows.Forms.Button { Text = "+ הוסף שורה", AutoSize = true, BackColor = Color.FromArgb(247, 201, 72) };
        var delRow = new System.Windows.Forms.Button { Text = "− מחק שורה", AutoSize = true, BackColor = Color.FromArgb(255, 228, 225) };
        var rowsInfo = new Label { AutoSize = true, ForeColor = Color.DimGray, Padding = new Padding(8, 7, 0, 0) };
        void FitAdj()
        {
            int n = data.ValueAdjustments.Count;
            adj.Height = adj.ColumnHeadersHeight + n * adj.RowTemplate.Height + 4;
            addRow.Enabled = n < MaxAdj;
            delRow.Text = n > 1 ? "− מחק שורה" : "− נקה שורה";
            rowsInfo.Text = $"{n} מתוך {MaxAdj} שורות. למחיקה: בחר שורה ולחץ Delete, או לחיצה ימנית ← מחק שורה.";
        }
        addRow.Click += (_, _) =>
        {
            if (data.ValueAdjustments.Count >= MaxAdj) return;
            adj.EndEdit();
            data.ValueAdjustments.Add(new ValueRow());
            FitAdj(); Changed();
            adj.CurrentCell = adj.Rows[adj.Rows.Count - 1].Cells[0];
        };
        void DeleteAdjRow(int i)
        {
            adj.EndEdit();
            if (i < 0 || i >= data.ValueAdjustments.Count) i = data.ValueAdjustments.Count - 1;
            var row = data.ValueAdjustments[i];
            bool has = !string.IsNullOrWhiteSpace(row.Subject) || row.Percent != null || row.Amount != null || !string.IsNullOrWhiteSpace(row.Notes);
            if (has && MessageBox.Show(this, $"למחוק את שורה {i + 1}" + (string.IsNullOrWhiteSpace(row.Subject) ? "" : $" (\"{row.Subject}\")") + "?", Text,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2, MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign) != DialogResult.Yes) return;
            if (data.ValueAdjustments.Count > 1) data.ValueAdjustments.RemoveAt(i);
            else { data.ValueAdjustments[0] = new ValueRow(); }
            adj.Refresh();
            FitAdj(); Changed();
        }
        delRow.Click += (_, _) => DeleteAdjRow(adj.CurrentRow?.Index ?? -1);

        // Delete key removes the selected row (while not typing inside a cell); right-click opens a row menu.
        adj.EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2;
        adj.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Delete && !adj.IsCurrentCellInEditMode && adj.CurrentRow != null)
            { e.Handled = true; DeleteAdjRow(adj.CurrentRow.Index); }
        };
        var menu = new ContextMenuStrip { RightToLeft = RightToLeft.Yes };
        var miDel = new ToolStripMenuItem("מחק שורה", null, (_, _) => DeleteAdjRow(adj.CurrentRow?.Index ?? -1)) { ShortcutKeyDisplayString = "Delete" };
        var miAdd = new ToolStripMenuItem("הוסף שורה", null, (_, _) => addRow.PerformClick());
        menu.Items.Add(miDel); menu.Items.Add(miAdd);
        menu.Opening += (_, _) =>
        {
            miAdd.Enabled = data.ValueAdjustments.Count < MaxAdj;
            miDel.Text = data.ValueAdjustments.Count > 1 ? $"מחק שורה {(adj.CurrentRow?.Index ?? 0) + 1}" : "נקה שורה";
        };
        adj.CellMouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Right || e.RowIndex < 0) return;
            adj.EndEdit();
            adj.CurrentCell = adj.Rows[e.RowIndex].Cells[Math.Max(0, e.ColumnIndex)];
        };
        adj.ContextMenuStrip = menu;
        adjBar.Controls.Add(addRow); adjBar.Controls.Add(delRow); adjBar.Controls.Add(rowsInfo);
        FitAdj();

        var raw = Grid(4);
        Add(raw, Lbl("אחוז הנזק הגולמי", true), Out(() => data.RawPercent == null ? "—" : Num.Pct(data.RawPercent)),
                 Lbl("חישוב לפי"), VatCombo(() => data.RawIncludesVat, v => data.RawIncludesVat = v));

        root.Controls.Add(Section("סיכום ההוצאות (כל המוקדים)"));
        root.Controls.Add(sum);
        root.Controls.Add(Section("פירוט שווי רכב בפועל"));
        root.Controls.Add(val);
        root.Controls.Add(adj);
        root.Controls.Add(adjBar);
        root.Controls.Add(raw);
        root.Controls.Add(Section("הערות (שורה לכל הערה)"));
        root.Controls.Add(Str(() => data.Notes, v => data.Notes = v, true, 130));
        root.Controls.Add(Section("לוט"));
        root.Controls.Add(Str(() => data.Attachments, v => data.Attachments = v));
        page.Controls.Add(root);
        return page;
    }

    // ---------- tab: depreciation opinion ----------
    TabPage DepreciationTab()
    {
        var page = Page("ירידת ערך");
        var root = Root();
        var inc = new CheckBox { Text = "לצרף חוות דעת לירידת ערך ל-PDF", Checked = data.IncludeDepreciation, AutoSize = true, Font = BoldFont };
        inc.CheckedChanged += (_, _) => { data.IncludeDepreciation = inc.Checked; Changed(); };
        var g = Grid(4);
        Add(g, Lbl("שווי הרכב בפועל"), Out(() => Num.Shekel(data.ActualValue)), Lbl("אחוז ירידת הערך"), Dec(() => data.DeprPercent, v => data.DeprPercent = v));
        Add(g, Lbl("סכום ירידת הערך", true), Out(() => Num.Shekel(data.DeprAmount)), new Label(), new Label());
        root.Controls.Add(inc);
        root.Controls.Add(g);
        root.Controls.Add(Lbl("שווי הרכב בפועל נלקח מלשונית \"סיכום ושווי רכב\"."));
        root.Controls.Add(Section("הסיבות לירידת הערך"));
        root.Controls.Add(Str(() => data.DeprReasons, v => data.DeprReasons = v, true, 200));
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
        Add(g, Lbl("תאריך אישור שכ\"ט"), Str(() => data.FeeDate, v => data.FeeDate = v));
        Add(g, Lbl("שכר טרחה"), Dec(() => data.FeeMain, v => data.FeeMain = v));
        Add(g, Lbl("צילומים"), Dec(() => data.FeePhotos, v => data.FeePhotos = v));
        Add(g, Lbl("נסיעות"), Dec(() => data.FeeTravel, v => data.FeeTravel = v));
        Add(g, Lbl("שונות"), Dec(() => data.FeeMisc, v => data.FeeMisc = v));
        Add(g, Lbl("עיגול (+/-)"), Dec(() => data.FeeRound, v => data.FeeRound = v));
        Add(g, Lbl("סה\"כ ללא מע\"מ", true), Out(() => Num.Shekel(data.FeeNet)));
        Add(g, Lbl("אחוז מע\"מ"), Dec(() => data.FeeVatPercent, v => data.FeeVatPercent = v ?? 0));
        Add(g, Lbl("מע\"מ"), Out(() => Num.Shekel(data.FeeVat)));
        Add(g, Lbl("סה\"כ כולל מע\"מ", true), Out(() => Num.Shekel(data.FeeGross)));
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
        var g1 = Grid(2); g1.MinimumSize = new Size(880, 0); g1.MaximumSize = new Size(880, 0);
        Add(g1, Lbl("שם המשרד (כותרת)"), T(() => s.OfficeName, v => s.OfficeName = v));
        Add(g1, Lbl("שם השמאי"), T(() => s.AppraiserName, v => s.AppraiserName = v));
        Add(g1, Lbl("כתובת המשרד"), T(() => s.Address, v => s.Address = v));
        Add(g1, Lbl("אימייל"), T(() => s.Email, v => s.Email = v));
        Add(g1, Lbl("פקס"), T(() => s.Fax, v => s.Fax = v));
        Add(g1, Lbl("נייד"), T(() => s.Mobile, v => s.Mobile = v));
        Add(g1, Lbl("מספר רישיון שמאי"), T(() => s.LicenseNo, v => s.LicenseNo = v));
        Add(g1, Lbl("השכלה והכשרה מקצועית"), T(() => s.Education, v => s.Education = v));
        Add(g1, Lbl("עוסק מורשה מספר"), T(() => s.VatDealerNo, v => s.VatDealerNo = v));
        Add(g1, Lbl("טקסט בתחתית העמוד"), T(() => s.FooterTag, v => s.FooterTag = v));
        var logoChk = new CheckBox { Text = "להציג את לוגו התוכנה בכותרת הדוח", Checked = s.ShowLogoInReport, AutoSize = true };
        logoChk.CheckedChanged += (_, _) => s.ShowLogoInReport = logoChk.Checked;
        Add(g1, Lbl("לוגו"), logoChk);
        Add(g1, Lbl("הערות ברירת מחדל"), T(() => s.DefaultNotes, v => s.DefaultNotes = v, true, 120));

        var arcRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        var arcBox = new TextBox { Width = 560, ReadOnly = true, Text = Storage.ArchiveDir(s), RightToLeft = RightToLeft.No };
        var arcPick = new System.Windows.Forms.Button { Text = "שנה תיקייה…", AutoSize = true };
        var arcOpen = new System.Windows.Forms.Button { Text = "פתח תיקייה", AutoSize = true };
        arcPick.Click += (_, _) =>
        {
            using var d = new FolderBrowserDialog { Description = "בחר את תיקיית הארכיון לחוות הדעת", InitialDirectory = arcBox.Text, UseDescriptionForTitle = true };
            if (d.ShowDialog(this) != DialogResult.OK) return;
            s.ArchiveDir = d.SelectedPath; arcBox.Text = Storage.ArchiveDir(s);
        };
        arcOpen.Click += (_, _) => { try { Process.Start(new ProcessStartInfo(Storage.ArchiveDir(s)) { UseShellExecute = true }); } catch (Exception ex) { Msg(ex.Message, MessageBoxIcon.Warning); } };
        arcRow.Controls.AddRange(new Control[] { arcBox, arcPick, arcOpen });

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

        root.Controls.Add(Section("פרטי המשרד והשמאי (כותרת, הצהרה וחתימה)"));
        root.Controls.Add(g1);
        root.Controls.Add(Section("תיקיית ארכיון (שמירת תיקים ו-PDF)"));
        root.Controls.Add(arcRow);
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
