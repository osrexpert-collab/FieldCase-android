using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ShumaDesktop;

/// <summary>Builds the expert-opinion PDF. Pages flow with the content, so there are never empty pages.</summary>
public static class PdfBuilder
{
    const string Yellow = "#FFF6C4";
    const string Gray = "#EDEDED";
    const string Line = "#555555";
    const string Font = "Arial";

    public static void Generate(CaseData d, OfficeSettings s, string path)
    {
        Document.Create(doc =>
        {
            // ---------- the opinion ----------
            doc.Page(page =>
            {
                Setup(page);
                page.Header().Element(h => Letterhead(h, s));
                page.Content().PaddingTop(6).Column(col =>
                {
                    col.Spacing(7);
                    Recipient(col, d);
                    col.Item().PaddingTop(4).AlignCenter().Text(t =>
                    {
                        t.Span(d.DocTitle + "  ").Bold().FontSize(16).Underline();
                        t.Span(d.OpinionNo).Bold().FontSize(16).Underline();
                    });
                    col.Item().Background(Yellow).Padding(5).Row(r =>
                    {
                        r.RelativeItem().Text("מבלי לפגוע בזכויות").Bold();
                        r.AutoItem().Text(d.RepairStatus ?? "").Bold();
                    });
                    Details(col, d);
                    col.Item().Background(Yellow).Padding(6).Text(
                        "בהתאם לבקשתכם בחנתי את נזקי הרכב כפי שהוצגו בפני, והערכתי אותם כמפורט להלן:\n" +
                        "הערכתי נעשתה מבלי לפגוע בזכויות ומתייחסת לנזקים שהוצגו בפני לפי נסיבות האירוע ובהתאם לגרסת בעל הרכב.");
                    Heading(col, "נסיבות המקרה:");
                    col.Item().Background(Yellow).Padding(6).Text(d.Circumstances ?? "");
                    Heading(col, "פירוט העבודות:");
                    col.Item().Background(Yellow).Padding(6).Text(
                        "לבקשתכם בדקנו את נזקי הרכב כפי שהוצגו בפני והערכתי אותם כמפורט להלן לתיקון הרכב.\nהיה צורך בביצוע העבודות הבאות:");

                    for (int i = 0; i < d.Foci.Count; i++) Focus(col, d.Foci[i], i + 1, d.Foci.Count);
                    Summary(col, d);
                    RawDamage(col, d);
                    Depreciation(col, d);

                    if (!string.IsNullOrWhiteSpace(d.Notes))
                    {
                        Heading(col, "הערות:");
                        col.Item().Background(Yellow).Padding(6).Text(d.Notes);
                    }
                    Heading(col, "לוט:");
                    col.Item().Background(Yellow).Padding(6).Text(d.Attachments ?? "");
                    Signature(col, s, true);
                });
                Footer(page);
            });

            // ---------- fee invoice ----------
            if (d.IncludeFee)
            {
                doc.Page(page =>
                {
                    Setup(page);
                    page.Header().Element(h => Letterhead(h, s));
                    page.Content().PaddingTop(6).Column(col =>
                    {
                        col.Spacing(8);
                        Recipient(col, d);
                        col.Item().PaddingTop(4).AlignCenter().Text(t =>
                        {
                            t.Span($"חשבון שכ\"ט {d.DocTitle}  ").Bold().FontSize(15).Underline();
                            t.Span(d.OpinionNo).Bold().FontSize(15).Underline();
                        });
                        Details(col, d);
                        col.Item().PaddingHorizontal(70).Table(t =>
                        {
                            t.ColumnsDefinition(c => { c.RelativeColumn(2); c.RelativeColumn(1); });
                            void R(string label, decimal? v, bool bold = false)
                            {
                                var l1 = t.Cell().Element(Cell).Text(label);
                                var l2 = t.Cell().Element(c => Cell(c).Background(Yellow)).AlignLeft().Text(Num.Money(v, 2));
                                if (bold) { l1.Bold(); l2.Bold(); }
                            }
                            R("שכר טרחה", d.FeeMain); R("צילומים", d.FeePhotos); R("נסיעות", d.FeeTravel);
                            R("שונות", d.FeeMisc); R("עיגול חשבון שכ\"ט", d.FeeRound);
                            R("סה\"כ ללא מע\"מ", d.FeeNet);
                            R($"מע\"מ לפי {Num.Plain(d.FeeVatPercent)}%", d.FeeVat);
                            R("סה\"כ כולל מע\"מ", d.FeeGross, true);
                        });
                        col.Item().PaddingTop(6).Text("חשבון זה אינו מהווה חשבונית מס. חשבונית מס תומצא לאחר קבלת התשלום.");
                        col.Item().Text("בברכה,");
                        Signature(col, s, false);
                    });
                    Footer(page);
                });
            }

            // ---------- photos ----------
            if (d.IncludePhotos && d.Photos.Count > 0)
            {
                doc.Page(page =>
                {
                    Setup(page);
                    page.Header().Element(h => Letterhead(h, s));
                    page.Content().PaddingTop(6).Column(col =>
                    {
                        col.Spacing(8);
                        col.Item().Background(Yellow).Padding(5).AlignCenter().Text("תמונות מקושרות לחוות דעת").Bold().FontSize(12);
                        col.Item().Table(t =>
                        {
                            t.ColumnsDefinition(c => { c.RelativeColumn(); c.RelativeColumn(); });
                            int n = 0;
                            foreach (var p in d.Photos)
                            {
                                n++;
                                byte[] bytes;
                                try { bytes = Convert.FromBase64String(p.JpegBase64); } catch { continue; }
                                t.Cell().Padding(4).Column(pc =>
                                {
                                    pc.Item().Height(190).Border(0.5f).BorderColor(Line).Padding(2).AlignCenter().AlignMiddle().Image(bytes).FitArea();
                                    pc.Item().PaddingTop(2).Text($"{n}. {p.Caption}").FontSize(9);
                                });
                            }
                            if (n % 2 == 1) t.Cell();
                        });
                    });
                    Footer(page);
                });
            }
        }).GeneratePdf(path);
    }

    // ---------- building blocks ----------

    static void Setup(PageDescriptor page)
    {
        page.Size(PageSizes.A4);
        page.Margin(28);
        page.ContentFromRightToLeft();
        page.PageColor(Colors.White);
        page.DefaultTextStyle(x => x.FontFamily(Font).FontSize(10).FontColor(Colors.Black));
    }

    static void Footer(PageDescriptor page) =>
        page.Footer().AlignCenter().Text(t =>
        {
            t.Span("עמוד ").FontSize(8).FontColor(Colors.Grey.Darken1);
            t.CurrentPageNumber().FontSize(8).FontColor(Colors.Grey.Darken1);
            t.Span(" מתוך ").FontSize(8).FontColor(Colors.Grey.Darken1);
            t.TotalPages().FontSize(8).FontColor(Colors.Grey.Darken1);
        });

    static void Letterhead(IContainer c, OfficeSettings s) =>
        c.BorderBottom(0.8f).BorderColor(Line).PaddingBottom(5).Column(col =>
        {
            col.Item().AlignCenter().Text(s.OfficeTitle ?? "").Bold().FontSize(15);
            col.Item().AlignCenter().Text($"{s.Address} · נייד: {s.Mobile} · פקס: {s.Fax} · {s.Email}").FontSize(8.5f).FontColor(Colors.Grey.Darken2);
        });

    static void Recipient(ColumnDescriptor col, CaseData d) =>
        col.Item().Row(r =>
        {
            r.RelativeItem().Column(c =>
            {
                c.Item().Text("לכבוד").Bold();
                if (!string.IsNullOrWhiteSpace(d.ToName)) c.Item().Text(d.ToName);
                if (!string.IsNullOrWhiteSpace(d.ToAddress)) c.Item().Text(d.ToAddress);
                c.Item().Text("טל: " + d.ToPhone);
            });
            r.ConstantItem(170).Column(c =>
            {
                c.Item().Text(t => { t.Span("תאריך  ").FontColor(Colors.Grey.Darken1); t.Span(d.DocDate); });
                c.Item().Text(t => { t.Span("תיק  ").FontColor(Colors.Grey.Darken1); t.Span(d.FileNo); });
                c.Item().Text(t => { t.Span("ע.מ.  ").FontColor(Colors.Grey.Darken1); t.Span(d.VatId); });
            });
        });

    static void Details(ColumnDescriptor col, CaseData d)
    {
        var right = new (string, string)[] { ("המזמין/בעל הרכב:", d.Owner), ("תאריך אירוע:", d.EventDate), ("תאריך בדיקה:", d.InspectionDate),
                                            ("מקום הבדיקה:", d.InspectionPlace), ("בעלויות:", d.Ownership), ("תוצר ודגם:", d.MakeModel) };
        var left = new (string, string)[] { ("מס' רישוי:", d.LicenseNo), ("מד אוץ:", d.Odometer), ("מס' שילדה:", d.Vin),
                                           ("שנת יצור:", d.Year), ("סוג רכב:", d.VehicleType), ("סוג חוות דעת:", d.OpinionType) };
        col.Item().Table(t =>
        {
            t.ColumnsDefinition(c => { c.ConstantColumn(95); c.RelativeColumn(); c.ConstantColumn(85); c.RelativeColumn(); });
            for (int i = 0; i < 6; i++)
            {
                t.Cell().Element(c => Cell(c).Background(Gray)).Text(right[i].Item1).Bold().FontSize(9);
                t.Cell().Element(Cell).Text(right[i].Item2 ?? "");
                t.Cell().Element(c => Cell(c).Background(Gray)).Text(left[i].Item1).Bold().FontSize(9);
                t.Cell().Element(Cell).Text(left[i].Item2 ?? "");
            }
        });
    }

    static void Focus(ColumnDescriptor col, FocusData f, int n, int count)
    {
        var name = string.IsNullOrWhiteSpace(f.Name) ? "" : " - " + f.Name;
        if (count > 1) Heading(col, $"מוקד {n}{name}");

        var works = f.Works.Where(w => !string.IsNullOrWhiteSpace(w.Description) || w.Cost != null).ToList();
        if (works.Count > 0)
        {
            col.Item().Table(t =>
            {
                t.ColumnsDefinition(c => { c.RelativeColumn(); c.ConstantColumn(110); });
                t.Header(h =>
                {
                    h.Cell().Element(c => Cell(c).Background(Gray)).AlignCenter().Text("תיאור עבודות").Bold();
                    h.Cell().Element(c => Cell(c).Background(Gray)).AlignCenter().Text("עלות עבודות").Bold();
                });
                foreach (var w in works)
                {
                    t.Cell().Element(Cell).Text(w.Description ?? "");
                    t.Cell().Element(Cell).AlignLeft().Text(Num.Money(w.Cost));
                }
                t.Cell().Element(c => Cell(c).Background(Gray)).Text("סה\"כ עבודות").Bold();
                t.Cell().Element(c => Cell(c).Background(Gray)).AlignLeft().Text(Num.Money(f.WorksTotal)).Bold();
            });
        }

        var parts = f.Parts.Where(p => !string.IsNullOrWhiteSpace(p.Description) || p.Price != null).ToList();
        if (parts.Count > 0)
        {
            col.Item().Table(t =>
            {
                t.ColumnsDefinition(c => { c.RelativeColumn(); c.ConstantColumn(95); c.ConstantColumn(95); });
                t.Header(h =>
                {
                    h.Cell().Element(c => Cell(c).Background(Gray)).AlignCenter().Text("תיאור החלק").Bold();
                    h.Cell().Element(c => Cell(c).Background(Gray)).AlignCenter().Text("סוג החלק").Bold();
                    h.Cell().Element(c => Cell(c).Background(Gray)).AlignCenter().Text("מחיר החלק").Bold();
                });
                foreach (var p in parts)
                {
                    t.Cell().Element(Cell).Text(p.Description ?? "");
                    t.Cell().Element(Cell).AlignCenter().Text(p.Type ?? "");
                    t.Cell().Element(Cell).AlignLeft().Text(Num.Money(p.Price));
                }
                t.Cell().ColumnSpan(2).Element(c => Cell(c).Background(Gray)).Text("סה\"כ חלקים").Bold();
                t.Cell().Element(c => Cell(c).Background(Gray)).AlignLeft().Text(Num.Money(f.PartsTotal)).Bold();
            });
        }
    }

    static void Summary(ColumnDescriptor col, CaseData d)
    {
        col.Item().PaddingTop(4).Table(t =>
        {
            t.ColumnsDefinition(c => { c.RelativeColumn(); c.ConstantColumn(120); });
            foreach (var f in d.Foci)
            {
                t.Cell().Element(c => Cell(c).Background(Yellow)).Text(t2 =>
                {
                    t2.Span("סה\"כ נזק למוקד ").Bold();
                    t2.Span(f.Name ?? "").Bold();
                });
                t.Cell().Element(Cell).AlignLeft().Text(Num.Money(f.Total)).Bold();
            }
            if (d.Foci.Count > 1)
            {
                t.Cell().Element(c => Cell(c).Background(Yellow)).Text("סה\"כ נזק (כל המוקדים)").Bold();
                t.Cell().Element(Cell).AlignLeft().Text(Num.Money(d.DamageTotal)).Bold();
            }
            if (d.TotalIncludesVat)
            {
                t.Cell().Element(c => Cell(c).Background(Yellow)).Text($"מע\"מ {Num.Plain(d.VatPercent)}%").Bold();
                t.Cell().Element(Cell).AlignLeft().Text(Num.Money(d.DamageVat, 2));
            }
            t.Cell().Element(c => Cell(c).Background(Yellow)).Text(d.TotalIncludesVat ? "סה\"כ כולל מע\"מ" : "סה\"כ לא כולל מע\"מ").Bold();
            t.Cell().Element(Cell).AlignLeft().Text(Num.Money(d.TotalFinal, d.TotalIncludesVat ? 2 : 0)).Bold();
        });
    }

    static void RawDamage(ColumnDescriptor col, CaseData d)
    {
        if (d.CarValue == null) return;
        Heading(col, "תחשיב נזק גולמי");
        var vat = d.CarValueIncludesVat ? "כולל מע\"מ" : "לא כולל מע\"מ";
        col.Item().Background(Yellow).Padding(6).Column(c =>
        {
            c.Item().Text($"ערך רכב בסיסי עובר ליום האירוע לפי מחירון לוי יצחק {Num.Money(d.CarValue)} ₪ {vat}" +
                          (string.IsNullOrWhiteSpace(d.ModelCode) ? "" : $", קוד דגם {d.ModelCode}"));
            c.Item().Text($"הנזק הגולמי בסך {Num.Money(d.DamageTotal)} ₪ מהווה {Num.Plain(d.RawPercent)}% מערך הרכב. בחישוב נזק גולמי אינם כלולים");
        });
        col.Item().Text("הוצאות נלוות לתיקון הרכב, שווי הרכב אינו כולל עבר ביטוחי, בעלויות ומד אוץ.");
    }

    static void Depreciation(ColumnDescriptor col, CaseData d)
    {
        var rows = d.Depreciation.Where(r => !string.IsNullOrWhiteSpace(r.Factor) || r.Percent != null).ToList();
        if (rows.Count == 0 && d.CarValueEvent == null) return;
        Heading(col, "ירידת ערך");
        col.Item().Text("נתבקשתי להביע את דעתי לגבי הפחתת הערך שגרמה לרכב הנדון בעקבות האירוע הנדון.");
        if (!string.IsNullOrWhiteSpace(d.DeprReason)) col.Item().Background(Yellow).Padding(5).Text(d.DeprReason);
        col.Item().Text("שיעור ירידת ערך ברכב נקבע ע\"פ הגורמים הבאים:");
        col.Item().Table(t =>
        {
            t.ColumnsDefinition(c => { c.RelativeColumn(); c.ConstantColumn(90); });
            t.Header(h =>
            {
                h.Cell().Element(c => Cell(c).Background(Gray)).AlignCenter().Text("גורם ירידת הערך").Bold();
                h.Cell().Element(c => Cell(c).Background(Gray)).AlignCenter().Text("אחוז").Bold();
            });
            foreach (var r in rows)
            {
                t.Cell().Element(Cell).Text(r.Factor ?? "");
                t.Cell().Element(Cell).AlignCenter().Text(Num.Plain(r.Percent) + (r.Percent != null ? "%" : ""));
            }
            t.Cell().Element(c => Cell(c).Background(Gray)).Text("סה\"כ למוקד (%)").Bold();
            t.Cell().Element(c => Cell(c).Background(Gray)).AlignCenter().Text(Num.Plain(d.DeprPercentTotal) + "%").Bold();
        });
        var vat = d.CarValueEventIncludesVat ? "כולל מע\"מ" : "לא כולל מע\"מ";
        col.Item().Background(Yellow).Padding(5).Column(c =>
        {
            c.Item().Text($"ערך הרכב משוערך ליום האירוע הינו {Num.Money(d.CarValueEvent)} ₪ {vat}").Bold();
            c.Item().Text($"לפיכך סה\"כ ירידת הערך {Num.Money(d.DeprTotal)} ₪").Bold();
        });
    }

    static void Signature(ColumnDescriptor col, OfficeSettings s, bool withLicense)
    {
        col.Item().PaddingTop(18).Row(r =>
        {
            r.RelativeItem().Column(c =>
            {
                c.Item().Text(s.AppraiserName ?? "");
                c.Item().Text(s.AppraiserSubtitle ?? "");
                if (withLicense && !string.IsNullOrWhiteSpace(s.LicenseNo)) c.Item().Text("שמאי רכב מס' " + s.LicenseNo);
            });
            r.ConstantItem(170).Column(c =>
            {
                if (!string.IsNullOrWhiteSpace(s.SignaturePngBase64))
                {
                    try { c.Item().Height(50).AlignCenter().Image(Convert.FromBase64String(s.SignaturePngBase64)).FitArea(); }
                    catch { c.Item().Height(50); }
                }
                else c.Item().Height(40);
                c.Item().BorderTop(0.8f).BorderColor(Colors.Black).AlignCenter().Text("חתימה").FontSize(8.5f);
            });
        });
    }

    static void Heading(ColumnDescriptor col, string text) =>
        col.Item().PaddingTop(4).Text(text).Bold().FontSize(11);

    static IContainer Cell(IContainer c) =>
        c.Border(0.6f).BorderColor(Line).PaddingVertical(3).PaddingHorizontal(4);
}
