using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShumaDesktop;

public class WorkRow
{
    public string Description { get; set; } = "";
    public decimal? Cost { get; set; }
}

public class PartRow
{
    public string Description { get; set; } = "";
    public string Type { get; set; } = "";
    public decimal? Price { get; set; }
}

public class DeprRow
{
    public string Factor { get; set; } = "";
    public decimal? Percent { get; set; }
}

public class FocusData
{
    public string Name { get; set; } = "";
    public BindingList<WorkRow> Works { get; set; } = new();
    public BindingList<PartRow> Parts { get; set; } = new();

    [JsonIgnore] public decimal WorksTotal => Works.Sum(w => w.Cost ?? 0);
    [JsonIgnore] public decimal PartsTotal => Parts.Sum(p => p.Price ?? 0);
    [JsonIgnore] public decimal Total => WorksTotal + PartsTotal;
}

public class PhotoData
{
    public string Caption { get; set; } = "";
    public string JpegBase64 { get; set; } = "";
}

public class CaseData
{
    // header
    public string DocTitle { get; set; } = "חוות דעת";
    public string OpinionNo { get; set; } = "";
    public string DocDate { get; set; } = DateTime.Today.ToString("dd/MM/yyyy");
    public string FileNo { get; set; } = "";
    public string VatId { get; set; } = "307233882";
    public string ToName { get; set; } = "";
    public string ToAddress { get; set; } = "";
    public string ToPhone { get; set; } = "";
    public string RepairStatus { get; set; } = "רכב תוקן בפועל";

    // vehicle
    public string Owner { get; set; } = "";
    public string EventDate { get; set; } = "";
    public string InspectionDate { get; set; } = "";
    public string InspectionPlace { get; set; } = "";
    public string Ownership { get; set; } = "";
    public string MakeModel { get; set; } = "";
    public string LicenseNo { get; set; } = "";
    public string Odometer { get; set; } = "";
    public string Vin { get; set; } = "";
    public string Year { get; set; } = "";
    public string VehicleType { get; set; } = "";
    public string OpinionType { get; set; } = "";

    public string Circumstances { get; set; } = "הרכב המצוין לעיל נפגע בחלקו ה_____ . ראה תיאור האירוע בטופס תביעה.";

    // damage
    public List<FocusData> Foci { get; set; } = new() { new FocusData { Name = "הקדמי" } };
    public decimal VatPercent { get; set; } = 18;
    public bool TotalIncludesVat { get; set; } = true;

    // raw damage
    public decimal? CarValue { get; set; }
    public bool CarValueIncludesVat { get; set; } = false;
    public string ModelCode { get; set; } = "";

    // depreciation
    public string DeprReason { get; set; } = "לרכב נגרמה ירידת ערך - הסיבות לירידת הערך באופן כללי הם: כמפורט בדוח תיקונים.";
    public BindingList<DeprRow> Depreciation { get; set; } = new();
    public decimal? CarValueEvent { get; set; }
    public bool CarValueEventIncludesVat { get; set; } = false;

    public string Notes { get; set; } = "";
    public string Attachments { get; set; } = "צילומים, חישוב ערך הרכב, רישיון רכב, חשבונית תיקון, ח. שכ\"ט.";

    // fee invoice
    public bool IncludeFee { get; set; } = true;
    public decimal? FeeMain { get; set; }
    public decimal? FeePhotos { get; set; }
    public decimal? FeeTravel { get; set; }
    public decimal? FeeMisc { get; set; }
    public decimal? FeeRound { get; set; }
    public decimal FeeVatPercent { get; set; } = 18;

    public bool IncludePhotos { get; set; } = true;
    public List<PhotoData> Photos { get; set; } = new();

    // ---- calculations ----
    [JsonIgnore] public decimal DamageTotal => Foci.Sum(f => f.Total);
    [JsonIgnore] public decimal DamageVat => Math.Round(DamageTotal * VatPercent / 100m, 2);
    [JsonIgnore] public decimal TotalFinal => TotalIncludesVat ? DamageTotal + DamageVat : DamageTotal;
    [JsonIgnore] public decimal? RawPercent => CarValue is > 0 ? Math.Round(DamageTotal / CarValue.Value * 100m, 1) : null;
    [JsonIgnore] public decimal DeprPercentTotal => Depreciation.Sum(d => d.Percent ?? 0);
    [JsonIgnore] public decimal DeprTotal => Math.Round((CarValueEvent ?? 0) * DeprPercentTotal / 100m, 0);
    [JsonIgnore] public decimal FeeNet => (FeeMain ?? 0) + (FeePhotos ?? 0) + (FeeTravel ?? 0) + (FeeMisc ?? 0) + (FeeRound ?? 0);
    [JsonIgnore] public decimal FeeVat => Math.Round(FeeNet * FeeVatPercent / 100m, 2);
    [JsonIgnore] public decimal FeeGross => FeeNet + FeeVat;
}

public class OfficeSettings
{
    public string OfficeTitle { get; set; } = "א.א.רבייב שמאי רכב. מהנדס מכונות כלי רכב - אקספרט";
    public string Address { get; set; } = "המלכים 126 רמת השרון";
    public string Mobile { get; set; } = "0535224100";
    public string Fax { get; set; } = "0774248362";
    public string Email { get; set; } = "osrexpert@gmail.com";
    public string AppraiserName { get; set; } = "א.א. רבייב – שמאי רכב";
    public string AppraiserSubtitle { get; set; } = "מהנדס מכונות - כלי רכב";
    public string LicenseNo { get; set; } = "";
    public string SignaturePngBase64 { get; set; } = "";
    public string DefaultNotes { get; set; } =
        "- לא ידוע לנו על עברו הביטוחי של הרכב ותאונות קודמות.\r\n" +
        "- לרכב נגרמה ירידת ערך מהאירוע הנדון.\r\n" +
        "- ערך הרכב נקבע בהתאם לנתונים הנמצאים במשרד.\r\n" +
        "- מס' שלדה זוהה, צולם ונמצא תואם לרישיונות הרכב.\r\n" +
        "- לא אושר מע\"מ מאחר ויש באפשרות בעלי הרכב להתקזז ישירות.\r\n" +
        "- הרכב שהה במוסך לצורך תיקונים כ- ___ יום (זמן עבודה בפועל).";
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

    public static string Money(decimal? v, int dec = 0) =>
        v is null ? "" : v.Value.ToString(dec == 0 ? "#,##0" : "#,##0.00", He);

    public static string Plain(decimal? v) => v is null ? "" : v.Value.ToString("0.##", CultureInfo.InvariantCulture);
}
