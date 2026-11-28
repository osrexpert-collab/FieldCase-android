using System.Globalization;

namespace ShumaDesktop;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        CultureInfo.DefaultThreadCurrentCulture = new CultureInfo("he-IL");
        CultureInfo.DefaultThreadCurrentUICulture = new CultureInfo("he-IL");
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
        ApplicationConfiguration.Initialize();
        var form = new MainForm();
        if (args.Length > 0 && File.Exists(args[0])) form.OpenCase(args[0]);
        Application.Run(form);
    }
}
