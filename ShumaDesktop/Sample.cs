namespace ShumaDesktop;

/// <summary>Sample case (from the office's example report). Used by "ShumaDesktop.exe --sample out.pdf" to check the PDF layout.</summary>
public static class Sample
{
    public static CaseData Case()
    {
        var d = new CaseData
        {
            OpinionNo = "1234", DocDate = "30/10/2026", Status = "פיצוי מוסכם",
            ToName = "אברגל אריה", ToAddress = "אחוזם 112,", ToZip = "79595",
            InsuredName = "אברגל אריה", Insurer = "חוות דעת פרטית", InspectingAppraiser = "ישראל ישראלי", EventDate = "10/07/2026",
            InspectionDate = "30/10/2026", InspectionPlace = "ראשון לציון", Garage = "מוסך השלום", CaseType = "פרטי", LicenseDate = "16/03/2022",
            InsuredId = "02795821-4",
            LicenseNo = "853-40-902", Manufacturer = "טסלה", Model = "MODEL 3 LONG RANGE", Year = "2022", ModelCode = "299200 טסלה מודל 3 לונג ריינג'",
            Odometer = "149,358", Vin = "LRW3E7EK3NC494360", Originality = "פרטי בבעלות פרטית", VehicleType = "פרטי", OwnersCount = "1", MotCode = "01327-0331",
            OwnerName = "אברגל אריה", OwnerAddress = "אחוזם 112", OwnerZip = "79595",
            Circumstances = "הרכב המצוין לעיל נפגע בחלקו הקדמי הימני. ראה תיאור האירוע: כתוצאה מפגיעה בו של צד ג'.",
            ListValue = 148820, IncludeDepreciation = true, DeprPercent = 1,
            DeprReasons = "במסגרת התיקון מוחלפת הכנף הקדמית הימנית ונצבעת. הכנף היא חלק פח חיצוני של מרכב הרכב, הגלוי לעין, והיא תורכב כשהיא צבועה בצבע שאינו צבע המקור של היצרן.",
            FeeMain = 800, FeePhotos = 50, FeeTravel = 100
        };
        d.Notes = new OfficeSettings().DefaultNotes;
        var f = d.Foci[0];
        f.Name = "נזק בחזית צד ימין";
        f.Works.Add(new CostRow { Description = "עבודות פחחות (6 ש\"ע × 350 ₪)", Cost = 2100 });
        f.Works.Add(new CostRow { Description = "עבודות חשמל (2 יחידות × 100 ₪)", Cost = 200 });
        f.Works.Add(new CostRow { Description = "כיוון מערך קדמי", Cost = 350 });
        f.Paint.Add(new CostRow { Description = "עבודות צבע — מגן קדמי, כנף קדמית ימנית (2 חלקים × 1,500 ₪)", Cost = 3000 });
        foreach (var (n, c, p) in new[] { ("מגן קדמי מושלם", "1519965-S0-B", 2262.71m), ("כנף קד' ימ'", "1081400-EC-D", 1271.19m), ("תומך כנף קד' ימ'", "1086016-SC-D", 97.46m),
                                          ("פנס ראשי ימין", "1514953-10-D", 5991.53m), ("פנס נסיעה באור יום ימ'", "1507933-10-C", 783.90m),
                                          ("מסגרת פנס נסיעה באור יום", "", 800m), ("חישוק 18\" קד' ימ'", "1234261-00-A", 1822.03m) })
            f.Parts.Add(new PartRow { Description = n, CatalogNo = c, Type = "מקורי", Price = p });
        var g = new FocusData { Name = "נזק בצד אחורי שמאל" };
        g.Works.Add(new CostRow { Description = "יישור דלת אחורית שמאל", Cost = 700 });
        g.Parts.Add(new PartRow { Description = "ידית דלת אחורית שמאל", CatalogNo = "1092222-00-A", Type = "תואם", Price = 320 });
        d.Foci.Add(g);
        var h = new FocusData { Name = "נזק בגג" };
        h.Paint.Add(new CostRow { Description = "צביעת גג", Cost = 1800 });
        d.Foci.Add(h);
        d.ValueAdjustments.Add(new ValueRow { Subject = "הפחתה בגין מס' ק\"מ 143,125", Sign = "-", Percent = 4.05m, Amount = 6027, Notes = "ק\"מ משוער ביום האירוע" });
        return d;
    }

    public static OfficeSettings Settings() => new() { AppraiserName = "ישראל ישראלי", LicenseNo = "1234", Education = "מהנדס מכונות" };
}
