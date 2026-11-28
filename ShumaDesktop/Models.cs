using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShumaDesktop;

public class CostRow
{
    public string Description { get; set; } = "";
    public decimal? Cost { get; set; }
}

public class PartRow
{
    public string Description { get; set; } = "";
    public string CatalogNo { get; set; } = "";
    public string Type { get; set; } = "מקורי";
    public decimal? Price { get; set; }
}

public class ValueRow
{
    public string Subject { get; set; } = "";
    public string Sign { get; set; } = "-";
    public decimal? Percent { get; set; }
    public decimal? Amount { get; set; }
    public string Notes { get; set; } = "";
}

public class FocusData
{
    public string Name { get; set; } = "";
    public BindingList<CostRow> Works { get; set; } = new();
    public BindingList<CostRow> Paint { get; set; } = new();
    public BindingList<PartRow> Parts { get; set; } = new();

    [JsonIgnore] public decimal WorksTotal => Works.Sum(w => w.Cost ?? 0);
    [JsonIgnore] public decimal PaintTotal => Paint.Sum(w => w.Cost ?? 0);
    [JsonIgnore] public decimal PartsTotal => Parts.Sum(p => p.Price ?? 0);
    [JsonIgnore] public decimal Total => WorksTotal + PaintTotal + PartsTotal;
}

public class PhotoData
{
    public string Caption { get; set; } = "";
    public string JpegBase64 { get; set; } = "";
}

public class CaseData
{
    // document
    public string DocTitle { get; set; } = "חוות דעת";
    public string OpinionNo { get; set; } = "";
    public string DocDate { get; set; } = DateTime.Today.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
    public string Status { get; set; } = "";
    public string ToName { get; set; } = "";
    public string ToAddress { get; set; } = "";
    public string ToZip { get; set; } = "";

    // insurance details
    public string InsuredName { get; set; } = "";
    public string Insurer { get; set; } = "";
    public string InspectingAppraiser { get; set; } = "";
    public string EventDate { get; set; } = "";
    public string InspectionDate { get; set; } = DateTime.Today.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
    public string InspectionPlace { get; set; } = "";
    public string Garage { get; set; } = "";
    public string CaseType { get; set; } = "פרטי";
    public string LicenseDate { get; set; } = "";
    public string PolicyNo { get; set; } = "";
    public string ClaimNo { get; set; } = "";
    public string InsuredId { get; set; } = "";
    public string InsuredPhone { get; set; } = "";
    public string Branch { get; set; } = "";

    // vehicle
    public string LicenseNo { get; set; } = "";
    public string Manufacturer { get; set; } = "";
    public string Model { get; set; } = "";
    public string Year { get; set; } = "";
    public string ModelCode { get; set; } = "";
    public string Odometer { get; set; } = "";
    public string Vin { get; set; } = "";
    public string Originality { get; set; } = "";
    public string VehicleType { get; set; } = "פרטי";
    public string OwnersCount { get; set; } = "";
    public string MotCode { get; set; } = "";

    // owner
    public string OwnerName { get; set; } = "";
    public string OwnerPhone { get; set; } = "";
    public string OwnerAddress { get; set; } = "";
    public string OwnerZip { get; set; } = "";

    // texts
    public string Intro { get; set; } = "המבוטח פנה למשרדינו וביקש לסכם את התביעה על בסיס גלובלי (נזק מוסכם). לאור האמור לעיל, חוות דעתנו סוכמה על בסיס ערך נזק מוסכם כולל מע\"מ. כל מחירי העבודות והחלקים הכלולים באומדן הנזק בחוות דעתנו נכונים ליום בדיקתנו.";
    public string Circumstances { get; set; } = "הרכב המצוין לעיל נפגע בחלקו ה_____. ראה תיאור האירוע: ";
    public string DamageDescription { get; set; } = "בהתאם לבקשתך/כם בדקנו את המכונית הנדונה כפי שהוצגה בפנינו. החלקים המפורטים בחלקים ניזוקו, יצאו מכלל שימוש.";

    // damage
    public List<FocusData> Foci { get; set; } = new() { new FocusData() };
    public decimal VatPercent { get; set; } = 18;
    public bool ShowVat { get; set; } = true;

    // actual vehicle value
    public decimal? ListValue { get; set; }
    public BindingList<ValueRow> ValueAdjustments { get; set; } = new();
    public bool RawIncludesVat { get; set; } = true;

    public string Notes { get; set; } = "";
    public string Attachments { get; set; } = "צילומים, חישוב ערך הרכב, רישיון רכב, ח. שכ\"ט.";

    // depreciation opinion
    public bool IncludeDepreciation { get; set; } = false;
    public decimal? DeprPercent { get; set; }
    public string DeprReasons { get; set; } = "";

    // fee invoice
    public bool IncludeFee { get; set; } = true;
    public string FeeDate { get; set; } = DateTime.Today.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
    public decimal? FeeMain { get; set; }
    public decimal? FeePhotos { get; set; }
    public decimal? FeeTravel { get; set; }
    public decimal? FeeMisc { get; set; }
    public decimal? FeeRound { get; set; }
    public decimal FeeVatPercent { get; set; } = 18;

    public bool IncludePhotos { get; set; } = true;
    public List<PhotoData> Photos { get; set; } = new();

    // ---- calculations ----
    public decimal FocusVat(FocusData f) => Math.Round(f.Total * VatPercent / 100m, 2);
    [JsonIgnore] public decimal Expenses => Foci.Sum(f => f.Total);
    [JsonIgnore] public decimal ExpensesVat => Math.Round(Expenses * VatPercent / 100m, 2);
    [JsonIgnore] public decimal ExpensesGross => Expenses + ExpensesVat;

    public decimal AdjustmentAmount(ValueRow r)
    {
        var amt = r.Amount ?? (r.Percent is decimal p && ListValue is decimal lv ? Math.Round(lv * p / 100m, 0) : 0);
        return (r.Sign == "+" ? 1 : -1) * amt;
    }
    [JsonIgnore] public decimal? ActualValue => ListValue is null ? null : ListValue + ValueAdjustments.Sum(AdjustmentAmount);
    [JsonIgnore] public decimal? RawPercent => ActualValue is > 0 ? Math.Round((RawIncludesVat ? ExpensesGross : Expenses) / ActualValue.Value * 100m, 2) : null;
    [JsonIgnore] public decimal? DeprAmount => ActualValue is decimal v && DeprPercent is decimal p ? Math.Round(v * p / 100m, 2) : null;

    [JsonIgnore] public decimal FeeNet => (FeeMain ?? 0) + (FeePhotos ?? 0) + (FeeTravel ?? 0) + (FeeMisc ?? 0) + (FeeRound ?? 0);
    [JsonIgnore] public decimal FeeVat => Math.Round(FeeNet * FeeVatPercent / 100m, 2);
    [JsonIgnore] public decimal FeeGross => FeeNet + FeeVat;
}

public class OfficeSettings
{
    public string OfficeName { get; set; } = "א.א.רבייב שמאי רכב. מהנדס מכונות כלי רכב - אקספרט";
    public string AppraiserName { get; set; } = "";
    public string Address { get; set; } = "המלכים 126 רמת השרון";
    public string Email { get; set; } = "osrexpert@gmail.com";
    public string Fax { get; set; } = "0774248362";
    public string Mobile { get; set; } = "0535224100";
    public string LicenseNo { get; set; } = "";
    public string Education { get; set; } = "";
    public string VatDealerNo { get; set; } = "307233882";
    public string FooterTag { get; set; } = "חוות דעת";
    public bool ShowLogoInReport { get; set; } = false;
    public string SignaturePngBase64 { get; set; } = "";
    public string DefaultNotes { get; set; } =
        "חוות דעתנו בוצעה בטרם בוצעו התיקונים בפועל ואינה כוללת את הנזקים הסמויים בשלב זה של הבדיקה.\r\n" +
        "לא ידוע לנו עברו הביטוחי של הרכב ותאונות קודמות.\r\n" +
        "לרכב נגרמה ירידת ערך מהאירוע הנדון.\r\n" +
        "ערך הרכב נקבע בהתאם לנתונים הנמצאים במשרדינו.\r\n" +
        "מחירי החלפים נבדקו ונמצאו תואמים.";
}

public static class Storage
{
    static readonly JsonSerializerOptions Opts = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static string AppDir
    {
        get
        {
            var d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ShumaDesktop");
            Directory.CreateDirectory(d);
            return d;
        }
    }

    public static OfficeSettings LoadSettings()
    {
        var p = Path.Combine(AppDir, "settings.json");
        try { if (File.Exists(p)) return JsonSerializer.Deserialize<OfficeSettings>(File.ReadAllText(p), Opts) ?? new(); }
        catch { }
        return new OfficeSettings();
    }

    public static void SaveSettings(OfficeSettings s) =>
        File.WriteAllText(Path.Combine(AppDir, "settings.json"), JsonSerializer.Serialize(s, Opts));

    public static CaseData LoadCase(string path) =>
        JsonSerializer.Deserialize<CaseData>(File.ReadAllText(path), Opts) ?? new CaseData();

    public static void SaveCase(CaseData c, string path) =>
        File.WriteAllText(path, JsonSerializer.Serialize(c, Opts));
}

public static class Num
{
    static readonly CultureInfo He = new("he-IL");

    public static decimal? Parse(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Replace("₪", "").Replace(",", "").Replace("%", "").Trim();
        if (decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var v)) return v;
        if (decimal.TryParse(s, NumberStyles.Number, He, out v)) return v;
        return null;
    }

    /// <summary>2,100.00 ₪</summary>
    public static string Shekel(decimal? v) => v is null ? "" : v.Value.ToString("#,##0.00", CultureInfo.InvariantCulture) + " ₪";
    public static string Money(decimal? v, int dec = 0) => v is null ? "" : v.Value.ToString(dec == 0 ? "#,##0" : "#,##0.00", CultureInfo.InvariantCulture);
    public static string Plain(decimal? v) => v is null ? "" : v.Value.ToString("0.##", CultureInfo.InvariantCulture);
    public static string Pct(decimal? v) => v is null ? "" : v.Value.ToString("0.00", CultureInfo.InvariantCulture) + "%";

    static readonly string[] Units = { "אפס", "אחד", "שניים", "שלושה", "ארבעה", "חמישה", "שישה", "שבעה", "שמונה", "תשעה",
                                       "עשרה", "אחד עשר", "שנים עשר", "שלושה עשר", "ארבעה עשר", "חמישה עשר", "שישה עשר", "שבעה עשר", "שמונה עשר", "תשעה עשר" };
    static readonly string[] Tens = { "", "", "עשרים", "שלושים", "ארבעים", "חמישים", "שישים", "שבעים", "שמונים", "תשעים" };

    /// <summary>Whole numbers 0-99 in Hebrew words (for VAT rates).</summary>
    public static string Words(decimal v)
    {
        if (v != Math.Floor(v) || v < 0 || v > 99) return Plain(v);
        int n = (int)v;
        if (n < 20) return Units[n];
        return n % 10 == 0 ? Tens[n / 10] : Tens[n / 10] + " ו" + Units[n % 10];
    }
}
