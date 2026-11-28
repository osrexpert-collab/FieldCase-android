using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ShumaDesktop;

/// <summary>
/// Builds the appraisal report in the office layout: damage opinion, optional depreciation opinion,
/// fee invoice and photos. Content flows across pages, so no page is ever left empty.
/// </summary>
public static class PdfBuilder
{
    const string Font = "Arial";
    const string Line = "#333333";
    const string Soft = "#666666";

    public static void Generate(CaseData d, OfficeSettings s, string path)
    {
        Document.Create(doc =>
        {
            doc.Page(p => { Setup(p, d, s); p.Content().PaddingTop(10).Column(col => DamageOpinion(col, d, s)); });
            if (d.IncludeDepreciation)
                doc.Page(p => { Setup(p, d, s); p.Content().PaddingTop(10).Column(col => DepreciationOpinion(col, d, s)); });
            if (d.IncludeFee)
                doc.Page(p => { Setup(p, d, s); p.Content().PaddingTop(10).Column(col => FeeInvoice(col, d, s)); });
            if (d.IncludePhotos && d.Photos.Count > 0)
                doc.Page(p => { Setup(p, d, s); p.Content().PaddingTop(10).Column(col => Photos(col, d)); });
        }).GeneratePdf(path);
    }

    // ================= documents =================

    static void DamageOpinion(ColumnDescriptor col, CaseData d, OfficeSettings s)
    {
        col.Spacing(6);
        Recipient(col, d);
        col.Item().PaddingTop(6).Row(r =>
        {
            r.RelativeItem().Text(t => { t.Span($"{d.DocTitle}: ").Bold().FontSize(13).Underline(); t.Span(d.OpinionNo ?? "").Bold().FontSize(13).Underline(); });
            r.ConstantItem(170).Column(c =>
            {
                c.Item().Text(t => { t.Span("תאריך:  "); t.Span(d.DocDate ?? ""); });
                if (!string.IsNullOrWhiteSpace(d.Status)) c.Item().Text(d.Status).Bold();
            });
        });

        // insurance + vehicle boxes
        col.Item().PaddingTop(8).Row(r =>
        {
            r.RelativeItem().BorderLeft(0.8f).BorderColor(Line).PaddingLeft(10).Column(c =>
            {
                BoxTitle(c, "פרטים ביטוחיים");
                KV(c, new[]
                {
                    ("שם מבוטח:", d.InsuredName), ("מבטח:", d.Insurer), ("שמאי בודק:", d.InspectingAppraiser), ("תאריך אירוע:", d.EventDate),
                    ("תאריך בדיקה:", Join(d.InspectionDate, string.IsNullOrWhiteSpace(d.InspectionPlace) ? "" : "ב-: " + d.InspectionPlace)),
                    ("מוסך:", d.Garage), ("סוג תיק:", d.CaseType), ("תאריך רישוי:", d.LicenseDate)
                });
            });
            r.ConstantItem(12);
            r.RelativeItem().Column(c =>
            {
                BoxTitle(c, "פרטי רכב");
                KV(c, new[]
                {
                    ("מספר רישוי:", d.LicenseNo), ("יצרן:", d.Manufacturer), ("דגם:", d.Model), ("שנת יצור:", d.Year), ("קוד דגם:", d.ModelCode),
                    ("מד אוץ:", string.IsNullOrWhiteSpace(d.Odometer) ? "" : d.Odometer + " (ביום הבדיקה)"), ("מספר שלדה:", d.Vin),
                    ("מקוריות רכב:", d.Originality), ("סוג רכב:", d.VehicleType), ("מספר בעלים:", d.OwnersCount), ("קוד משרד התחבורה:", d.MotCode)
                });
            });
        });
        Rule(col);
        col.Item().Column(c => BoxTitle(c, "פרטי בעל הרכב"));
        KV4(col, new[] { ("שם:", d.OwnerName, "מספר טלפון:", d.OwnerPhone), ("כתובת:", d.OwnerAddress, "מיקוד:", d.OwnerZip) });
        Rule(col);

        for (int i = 0; i < d.Foci.Count; i++)
        {
            var f = d.Foci[i];
            col.Item().EnsureSpace(120).PaddingTop(6).Border(0.8f).BorderColor(Line).PaddingVertical(2).AlignCenter()
               .Text(string.IsNullOrWhiteSpace(f.Name) ? $"מוקד נזק {i + 1}" : f.Name).Bold();
            if (i == 0)
            {
                Para(col, "מבוא:", d.Intro);
                Para(col, "נסיבות המקרה:", d.Circumstances);
                Para(col, "תיאור הנזקים:", d.DamageDescription);
            }
            Focus(col, d, f, i);
        }

        // totals of all foci
        Rule(col);
        Totals(col, new[]
        {
            ("סה\"כ הוצאות", Num.Shekel(d.Expenses), false),
            ($"מע\"מ ({Num.Plain(d.VatPercent)}%[{Num.Words(d.VatPercent)}])", Num.Shekel(d.ExpensesVat), false),
            ("סה\"כ כולל מע\"מ", Num.Shekel(d.ExpensesGross), true)
        });
        Rule(col);

        // summary
        col.Item().PaddingTop(6).Border(0.8f).BorderColor(Line).PaddingVertical(3).AlignCenter().Text("סיכום חוות הדעת").FontSize(14);
        if (d.ListValue != null)
        {
            col.Item().PaddingTop(6).Text("פירוט שווי רכב בפועל").Bold().Underline();
            col.Item().Table(t =>
            {
                t.ColumnsDefinition(c => { c.RelativeColumn(3); c.ConstantColumn(30); c.ConstantColumn(55); c.ConstantColumn(90); c.RelativeColumn(2); });
                foreach (var h in new[] { "נושא", "-/+", "באחוז", "סכום", "הערות" })
                    t.Cell().BorderBottom(0.8f).BorderColor(Line).PaddingVertical(2).Text(h).Bold();
                Row5(t, "ערך רכב מחירון", "", "", Num.Shekel(d.ListValue), "");
                foreach (var a in d.ValueAdjustments.Where(a => !string.IsNullOrWhiteSpace(a.Subject) || a.Amount != null || a.Percent != null))
                    Row5(t, a.Subject, a.Sign, a.Percent == null ? "" : Num.Pct(a.Percent), Num.Shekel(Math.Abs(d.AdjustmentAmount(a))), a.Notes);
                t.Cell().BorderTop(0.8f).BorderColor(Line).PaddingVertical(2).Text("סה\"כ שווי ערך רכב בפועל").Bold();
                t.Cell().BorderTop(0.8f).BorderColor(Line);
                t.Cell().BorderTop(0.8f).BorderColor(Line);
                t.Cell().BorderTop(0.8f).BorderColor(Line).PaddingVertical(2).Text(Num.Shekel(d.ActualValue)).Bold();
                t.Cell().BorderTop(0.8f).BorderColor(Line);
            });
            if (d.RawPercent != null)
                col.Item().PaddingTop(6).Text($"בהתאם לתחשיב הנ\"ל, אחוז הנזק הגולמי מסתכם בשיעור של {Num.Pct(d.RawPercent)} {(d.RawIncludesVat ? "כולל" : "לא כולל")} מע\"מ.");
        }

        var notes = (d.Notes ?? "").Split('\n').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
        if (notes.Count > 0)
        {
            col.Item().PaddingTop(8).Text("הערות:").Bold();
            foreach (var n in notes) col.Item().Text(n);
        }
        if (!string.IsNullOrWhiteSpace(d.Attachments)) Para(col, "לוט:", d.Attachments);
        Declaration(col, s);
        SignatureBlock(col, s, "שמאי מאשר");
    }

    static void Focus(ColumnDescriptor col, CaseData d, FocusData f, int index)
    {
        var name = string.IsNullOrWhiteSpace(f.Name) ? $"מוקד {index + 1}" : f.Name;
        var works = f.Works.Where(w => !string.IsNullOrWhiteSpace(w.Description) || w.Cost != null).ToList();
        if (works.Count > 0)
        {
            Section(col, "עבודות", t =>
            {
                t.ColumnsDefinition(c => { c.RelativeColumn(); c.ConstantColumn(100); });
                t.Header(h => { h.Cell().Text("תיאור עבודה").Bold(); h.Cell().Text("עלות עבודה").Bold(); });
                foreach (var w in works)
                {
                    t.Cell().PaddingVertical(1).Text(w.Description ?? "");
                    t.Cell().PaddingVertical(1).Text(Num.Shekel(w.Cost));
                }
                t.Cell().Text("סה\"כ עבודות ועבודות נלוות").Bold();
                t.Cell().BorderTop(0.8f).BorderColor(Line).Text(Num.Shekel(f.WorksTotal)).Bold();
            });
        }
        var paint = f.Paint.Where(w => !string.IsNullOrWhiteSpace(w.Description) || w.Cost != null).ToList();
        if (paint.Count > 0)
        {
            Section(col, "צבע", t =>
            {
                t.ColumnsDefinition(c => { c.RelativeColumn(); c.ConstantColumn(100); });
                foreach (var w in paint)
                {
                    t.Cell().PaddingVertical(1).Text(w.Description ?? "");
                    t.Cell().PaddingVertical(1).Text(Num.Shekel(w.Cost));
                }
                t.Cell().Text("סה\"כ עבודות צבע").Bold();
                t.Cell().BorderTop(0.8f).BorderColor(Line).Text(Num.Shekel(f.PaintTotal)).Bold();
            });
        }
        var parts = f.Parts.Where(p => !string.IsNullOrWhiteSpace(p.Description) || p.Price != null).ToList();
        if (parts.Count > 0)
        {
            Section(col, "חלקי חילוף", t =>
            {
                t.ColumnsDefinition(c => { c.RelativeColumn(3); c.RelativeColumn(2); c.ConstantColumn(60); c.ConstantColumn(100); });
                t.Header(h =>
                {
                    h.Cell().Text("תיאור החלק").Bold();
                    h.Cell().Text("מק\"ט").Bold();
                    h.Cell().Text("סוג החלק").Bold();
                    h.Cell().Text("סה\"כ מחיר").Bold();
                });
                foreach (var p in parts)
                {
                    t.Cell().PaddingVertical(1).Text(p.Description ?? "");
                    Val(t.Cell().PaddingVertical(1), p.CatalogNo);
                    t.Cell().PaddingVertical(1).Text(p.Type ?? "");
                    t.Cell().PaddingVertical(1).Text(Num.Shekel(p.Price));
                }
                t.Cell().ColumnSpan(3).Text("מחיר חלקי חילוף").Bold();
                t.Cell().BorderTop(0.8f).BorderColor(Line).Text(Num.Shekel(f.PartsTotal)).Bold();
            });
        }
        Rule(col);
        Totals(col, new[]
        {
            ($"סה\"כ נזק למוקד {name}", Num.Shekel(f.Total), true),
            ($"סה\"כ מע\"מ למוקד - {name}", Num.Shekel(d.FocusVat(f)), false),
            ($"סה\"כ נזק כולל מע\"מ למוקד - {name}", Num.Shekel(f.Total + d.FocusVat(f)), true)
        });
    }

    static void DepreciationOpinion(ColumnDescriptor col, CaseData d, OfficeSettings s)
    {
        col.Spacing(6);
        Recipient(col, d);
        col.Item().PaddingTop(6).Row(r =>
        {
            r.RelativeItem().Text("חוות דעת - לירידת ערך").Bold().FontSize(13).Underline();
            r.ConstantItem(170).Text(t => { t.Span("תאריך:  "); t.Span(d.DocDate ?? ""); });
        });
        col.Item().Text("א.ג.נ.");
        Rule(col);
        col.Item().Text("פרטי מבוטח").Bold().Underline();
        KV4(col, new[]
        {
            ("שם מבוטח:", d.InsuredName, "טלפון:", d.InsuredPhone),
            ("כתובת:", Join(d.OwnerAddress, string.IsNullOrWhiteSpace(d.OwnerZip) ? "" : "מיקוד " + d.OwnerZip), "", ""),
            ("המבטחים:", d.Insurer, "הסניף:", d.Branch),
            ("יצרן:", d.Manufacturer, "", ""),
            ("דגם:", d.Model, "שנת יצור:", d.Year),
            ("מספר רישוי:", d.LicenseNo, "מספר שלדה:", d.Vin),
            ("מד אוץ:", d.Odometer, "תאריך נזק:", d.EventDate)
        });
        col.Item().PaddingTop(8).Text($"1.  בתאריך {d.DocDate} אמדנו את שווי ירידת הערך לרכב הנ\"ל בהסתמך על אומדן נזק מספר {d.OpinionNo}");
        col.Item().Text($"2.  שווי הרכב בפועל: {Num.Shekel(d.ActualValue)}").Bold();
        col.Item().Text($"3.  אחוז ירידת הערך: {Num.Pct(d.DeprPercent)}     סכום ירידת הערך: {Num.Shekel(d.DeprAmount)}").Bold();
        col.Item().Text("4.  הסיבות לירידת הערך באופן כללי הם:");
        if (!string.IsNullOrWhiteSpace(d.DeprReasons)) col.Item().PaddingRight(14).Text(d.DeprReasons);
        col.Item().PaddingRight(14).Text("ירידת הערך אינה חייבת במע\"מ.");
        Declaration(col, s);
        SignatureBlock(col, s, "בכבוד רב");
        col.Item().PaddingTop(16).Text($"לוטה: העתק אומדן נזק מספר {d.OpinionNo}");
    }

    static void FeeInvoice(ColumnDescriptor col, CaseData d, OfficeSettings s)
    {
        col.Spacing(6);
        Recipient(col, d);
        col.Item().PaddingTop(6).Row(r =>
        {
            r.RelativeItem().Text("א.ג.נ.");
            r.ConstantItem(220).Text(t => { t.Span("תאריך אישור שכ\"ט:  "); t.Span(d.FeeDate ?? ""); });
        });
        col.Item().PaddingTop(8).AlignCenter().Text($"חשבון שכ\"ט {DocNoun(d.DocTitle)} מספר {d.OpinionNo}").Bold().FontSize(13).Underline();
        col.Item().PaddingTop(6).Text("פרטי הבדיקה").Bold().Underline();
        KV4(col, new[]
        {
            ("שם השמאי המטפל:", First(d.InspectingAppraiser, s.AppraiserName), "טלפון:", s.Mobile),
            ("תאריך בדיקה:", d.InspectionDate, "מקום בדיקה:", d.InspectionPlace),
            ("שם המוסך:", d.Garage, "", "")
        });
        col.Item().PaddingTop(6).Text("פרטים ביטוחיים").Bold().Underline();
        KV4(col, new[]
        {
            ("חברת ביטוח:", d.Insurer, "טלפון מבוטח:", d.InsuredPhone),
            ("שם מבוטח:", d.InsuredName, "בעל רכב:", d.OwnerName),
            ("מספר פוליסה:", d.PolicyNo, "מספר תביעה:", d.ClaimNo),
            ("מספר רישוי:", d.LicenseNo, "שנת יצור:", d.Year),
            ("יצרן:", d.Manufacturer, "דגם:", d.Model),
            ("תאריך נזק:", d.EventDate, "", ""),
            ("ת.ז. מבוטח:", d.InsuredId, "", "")
        });
        col.Item().PaddingTop(10).Table(t =>
        {
            t.ColumnsDefinition(c => { c.ConstantColumn(120); c.ConstantColumn(100); c.RelativeColumn(); });
            void R(string label, decimal? v, bool bold, bool line)
            {
                var a = t.Cell().PaddingVertical(1).Text(label);
                IContainer cell = line ? t.Cell().BorderTop(0.8f).BorderColor(Line) : t.Cell();
                var b = cell.PaddingVertical(1).Text(Num.Shekel(v ?? 0));
                t.Cell();
                if (bold) { a.Bold(); b.Bold(); }
            }
            R("שכר טרחה:", d.FeeMain, false, false);
            R("צילומים:", d.FeePhotos, false, false);
            R("נסיעות:", d.FeeTravel, false, false);
            R("שונות:", d.FeeMisc, false, false);
            R("סה\"כ ללא מע\"מ:", d.FeeNet, false, true);
            R($"מע\"מ לפי: ({Num.Plain(d.FeeVatPercent)}%)", d.FeeVat, false, false);
            R("סה\"כ כולל מע\"מ:", d.FeeGross, true, true);
            t.Cell().PaddingTop(4).Text("עוסק מורשה מספר:");
            t.Cell().PaddingTop(4).Text(s.VatDealerNo ?? "");
            t.Cell().PaddingTop(4).Text("(מסמך זה אינו מהווה אסמכתה לשלטונות המס)").FontSize(9);
        });
        col.Item().PaddingTop(18).AlignLeft().Width(200).Column(c =>
        {
            c.Item().Text("בכבוד רב  " + (s.AppraiserName ?? ""));
            if (!string.IsNullOrWhiteSpace(s.LicenseNo)) c.Item().Text("רישיון " + s.LicenseNo);
            Signature(c, s);
        });
    }

    static void Photos(ColumnDescriptor col, CaseData d)
    {
        col.Spacing(8);
        col.Item().Border(0.8f).BorderColor(Line).PaddingVertical(2).AlignCenter().Text("תמונות קשורות לחוות דעת").Bold();
        col.Item().Table(t =>
        {
            t.ColumnsDefinition(c => { c.RelativeColumn(); c.RelativeColumn(); });
            int n = 0;
            foreach (var p in d.Photos)
            {
                byte[] bytes;
                try { bytes = Convert.FromBase64String(p.JpegBase64); } catch { continue; }
                n++;
                var num = n;
                t.Cell().Padding(4).Column(pc =>
                {
                    pc.Item().Height(200).Border(0.5f).BorderColor(Soft).Padding(2).AlignCenter().AlignMiddle().Image(bytes).FitArea();
                    pc.Item().PaddingTop(2).Text($"{num}. {p.Caption}").FontSize(9);
                });
            }
            if (n % 2 == 1) t.Cell();
        });
    }

    // ================= building blocks =================

    static void Setup(PageDescriptor page, CaseData d, OfficeSettings s)
    {
        page.Size(PageSizes.A4);
        page.MarginHorizontal(40);
        page.MarginVertical(26);
        page.ContentFromRightToLeft();
        page.PageColor(Colors.White);
        page.DefaultTextStyle(x => x.FontFamily(Font).FontSize(10).FontColor(Colors.Black));
        page.Header().BorderBottom(0.8f).BorderColor(Line).PaddingBottom(6).Column(c =>
        {
            c.Item().AlignCenter().Text(s.OfficeName ?? "").Bold().FontSize(12);
            if (!string.IsNullOrWhiteSpace(s.AppraiserName)) c.Item().AlignCenter().Text(s.AppraiserName);
            if (!string.IsNullOrWhiteSpace(s.Address)) c.Item().AlignCenter().Text(s.Address);
            var contact = string.Join("  -  ", new[] { s.Email, Prefix("פקס ", s.Fax), Prefix("נייד ", s.Mobile) }.Where(x => !string.IsNullOrWhiteSpace(x)));
            c.Item().AlignCenter().Text(contact).FontSize(9);
        });
        page.Footer().BorderTop(0.6f).BorderColor(Line).PaddingTop(3).Text(t =>
        {
            t.Span($"{s.FooterTag} — {d.LicenseNo}").Bold().FontSize(7.5f).FontColor(Soft);
            t.Span($"        תאריך הדפסה: {DateTime.Today.ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture)}        עמוד: ").FontSize(7.5f).FontColor(Soft);
            t.CurrentPageNumber().FontSize(7.5f).FontColor(Soft);
        });
    }

    static void Recipient(ColumnDescriptor col, CaseData d) =>
        col.Item().Column(c =>
        {
            c.Item().Text("לכבוד: " + (d.ToName ?? ""));
            var addr = Join(d.ToAddress, string.IsNullOrWhiteSpace(d.ToZip) ? "" : "מיקוד: " + d.ToZip);
            if (addr.Length > 0) c.Item().Text(addr);
        });

    static void BoxTitle(ColumnDescriptor c, string text) =>
        c.Item().PaddingBottom(6).AlignRight().Border(0.8f).BorderColor(Line).PaddingHorizontal(10).PaddingVertical(1).Text(text).FontSize(13);

    /// <summary>Section title box and its table as one block, so a title is never left alone at the bottom of a page.</summary>
    static void Section(ColumnDescriptor col, string title, Action<TableDescriptor> table) =>
        col.Item().EnsureSpace(150).Column(sc =>
        {
            sc.Item().PaddingTop(8).PaddingBottom(4).AlignRight().MinWidth(110).Border(0.8f).BorderColor(Line).PaddingHorizontal(8).PaddingVertical(1).Text(title).FontSize(13);
            sc.Item().Table(table);
        });

    static void KV(ColumnDescriptor c, (string, string)[] rows) =>
        c.Item().Table(t =>
        {
            t.ColumnsDefinition(x => { x.ConstantColumn(92); x.RelativeColumn(); });
            foreach (var (k, v) in rows)
            {
                t.Cell().PaddingVertical(1).Text(k).FontColor(Soft);
                Val(t.Cell().PaddingVertical(1), v);
            }
        });

    static void KV4(ColumnDescriptor col, (string, string, string, string)[] rows) =>
        col.Item().Table(t =>
        {
            t.ColumnsDefinition(x => { x.ConstantColumn(95); x.RelativeColumn(); x.ConstantColumn(85); x.RelativeColumn(); });
            foreach (var (k1, v1, k2, v2) in rows)
            {
                t.Cell().PaddingVertical(1).Text(k1).FontColor(Soft);
                Val(t.Cell().PaddingVertical(1), v1);
                t.Cell().PaddingVertical(1).Text(k2).FontColor(Soft);
                Val(t.Cell().PaddingVertical(1), v2);
            }
        });

    static void Totals(ColumnDescriptor col, (string, string, bool)[] rows) =>
        col.Item().ShowEntire().Table(t =>
        {
            t.ColumnsDefinition(c => { c.RelativeColumn(); c.ConstantColumn(100); });
            foreach (var (label, value, bold) in rows)
            {
                var a = t.Cell().PaddingVertical(1).Text(label);
                var b = t.Cell().PaddingVertical(1).Text(value);
                if (bold) { a.Bold(); b.Bold(); }
            }
        });

    static void Row5(TableDescriptor t, string a, string b, string c, string d, string e)
    {
        t.Cell().PaddingVertical(1).Text(a ?? "");
        t.Cell().PaddingVertical(1).AlignCenter().Text(b ?? "");
        t.Cell().PaddingVertical(1).Text(c ?? "");
        t.Cell().PaddingVertical(1).Text(d ?? "");
        t.Cell().PaddingVertical(1).Text(e ?? "").FontSize(9);
    }

    static void Para(ColumnDescriptor col, string title, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        col.Item().PaddingTop(4).Text(title).Bold();
        col.Item().Text(text);
    }

    static void Rule(ColumnDescriptor col) => col.Item().PaddingVertical(4).LineHorizontal(0.8f).LineColor(Line);

    static void Declaration(ColumnDescriptor col, OfficeSettings s)
    {
        col.Item().PaddingTop(12).Text("אני הח\"מ: " + string.Join(", ", new[] { s.AppraiserName, s.Education }.Where(x => !string.IsNullOrWhiteSpace(x))));
        col.Item().Text("עוסק בשמאות רכב, תעודת שמאי מספר " + s.LicenseNo);
        col.Item().Text("נותן על פי בקשתך/כם חוות דעתי במקום עדות בשבועה בבית משפט. הנני מצהיר כי ידוע לי היטב על פי הוראות החוק הפלילי בדבר מתן עדות שקר בבית משפט, דין חוות דעת זו כשהיא חתומה על ידי, כדין עדות בשבועה בבית משפט.");
    }

    static void SignatureBlock(ColumnDescriptor col, OfficeSettings s, string title) =>
        col.Item().PaddingTop(14).AlignLeft().Width(200).Column(c =>
        {
            c.Item().Text(title).Bold();
            c.Item().Text(s.AppraiserName ?? "");
            c.Item().Text("רישיון מספר " + s.LicenseNo);
            Signature(c, s);
        });

    static void Signature(ColumnDescriptor c, OfficeSettings s)
    {
        if (string.IsNullOrWhiteSpace(s.SignaturePngBase64)) return;
        byte[] img;
        try { img = Convert.FromBase64String(s.SignaturePngBase64); } catch { return; }
        c.Item().PaddingTop(4).Height(50).AlignRight().Image(img).FitHeight();
    }

    static bool IsLtr(string v) => !string.IsNullOrEmpty(v) && v.Any(ch => ch < 128 && char.IsLetter(ch)) && !v.Any(ch => ch >= '\u0590' && ch <= '\u05FF');

    /// <summary>Writes a value; codes such as catalog numbers keep their left-to-right order.</summary>
    static TextBlockDescriptor Val(IContainer c, string v) =>
        IsLtr(v) ? c.AlignRight().ContentFromLeftToRight().Text(v) : c.Text(v ?? "");

    static string Prefix(string p, string v) => string.IsNullOrWhiteSpace(v) ? "" : p + v;

    static string Join(string a, string b) =>
        string.Join(" ", new[] { a, b }.Where(x => !string.IsNullOrWhiteSpace(x))).Trim();

    static string First(string a, string b) => string.IsNullOrWhiteSpace(a) ? b ?? "" : a;

    static string DocNoun(string title) => title == "חוות דעת" ? "חוות דעתנו" : title;
}
