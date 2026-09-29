using System.Windows;
using System.IO;
using PinCreator.Models;
using PinCreator.Services;

namespace PinCreator;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Length >= 3 && e.Args[0].Equals("--render", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var title = e.Args.Length > 3 ? e.Args[3] : "Create something worth saving";
                var renderer = new PinRenderer();
                var content = new PinContent(e.Args[1], title, "Made with Pin Creator Studio", "INSPIRATION", "Save this", "NEW", "yourbrand.com");
                var bitmap = renderer.Render(content, LayoutCatalog.All[0], new PinSize("Pinterest standard", 1000, 1500));
                renderer.Save(bitmap, e.Args[2]);
                Shutdown(0);
            }
            catch (Exception ex)
            {
                File.WriteAllText(e.Args[2] + ".error.txt", ex.ToString());
                Shutdown(1);
            }
            return;
        }

        new MainWindow().Show();
    }
}
