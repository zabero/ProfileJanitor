using System;
using System.IO;
using System.Windows;

namespace ProfileJanitor
{
    public partial class App : Application
    {
        // Nieobsluzony blad okna trafia tutaj - bez tego program znika bez sladu.
        public static readonly string CrashLog = Path.Combine(Path.GetTempPath(), "ProfileJanitor-crash.txt");

        // "--screenshot plik.png [--dark] [--select] [--helpwin] [--lang pl|en]": zapisuje obraz okna i konczy. Sluzy do ogladania
        // interfejsu tam, gdzie nie ma pulpitu (testy na maszynie wirtualnej); niczego nie usuwa.
        static int Screenshot(string[] args, string file)
        {
            int li = Array.IndexOf(args, "--lang");
            L.Init(li >= 0 && li + 1 < args.Length ? args[li + 1] : null);
            var app = new App();
            app.InitializeComponent();
            var win = new MainWindow { WindowStartupLocation = WindowStartupLocation.Manual, Left = 0, Top = 0, ShowInTaskbar = false };
            win.Loaded += async (s, e) =>
            {
                try
                {
                    await win.Ready;
                    Wpf.Ui.Appearance.ApplicationThemeManager.Apply(Array.IndexOf(args, "--dark") >= 0 ? Wpf.Ui.Appearance.ApplicationTheme.Dark : Wpf.Ui.Appearance.ApplicationTheme.Light);
                    if (Array.IndexOf(args, "--select") >= 0) win.DemoSelect();
                    await System.Threading.Tasks.Task.Delay(1200); // niech uklad i animacje sie uspokoja
                    Window shown = win;
                    if (Array.IndexOf(args, "--helpwin") >= 0)
                    {
                        shown = Help.Create(null);
                        shown.Show();
                        await System.Threading.Tasks.Task.Delay(800);
                    }
                    var root = (FrameworkElement)shown.Content;
                    var rect = new Rect(0, 0, root.ActualWidth, root.ActualHeight);
                    var dv = new System.Windows.Media.DrawingVisual();
                    using (var dc = dv.RenderOpen())
                    {
                        dc.DrawRectangle((System.Windows.Media.Brush)win.FindResource("ApplicationBackgroundBrush"), null, rect); // tlo okna rysuje system, nie WPF
                        dc.DrawRectangle(new System.Windows.Media.VisualBrush(root), null, rect);
                    }
                    var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap((int)rect.Width, (int)rect.Height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                    bmp.Render(dv);
                    var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
                    using (var f = File.Create(file)) enc.Save(f);
                    app.Shutdown(0);
                }
                catch (Exception ex)
                {
                    try { File.WriteAllText(CrashLog, DateTime.Now + "\r\n" + ex); } catch (Exception) { }
                    app.Shutdown(3);
                }
            };
            return app.Run(win);
        }

        static string Arg(string[] args, string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        // "--uitest --victim SID [--loaded SID] [--lang pl|en]": test okna od srodka (patrz MainWindow.UiTestAsync).
        // UWAGA: naprawde usuwa profil --victim. Tylko na maszynie testowej; uruchamia go vm-test.ps1.
        static int UiTest(string[] args)
        {
            L.Init(Arg(args, "--lang"));
            var app = new App();
            app.InitializeComponent();
            var win = new MainWindow { WindowStartupLocation = WindowStartupLocation.Manual, Left = 0, Top = 0, ShowInTaskbar = false };
            int code = 3;
            win.Loaded += async (s, e) =>
            {
                using (var o = new StreamWriter(Console.OpenStandardOutput(), new System.Text.UTF8Encoding(false)))
                {
                    try { code = await win.UiTestAsync(Arg(args, "--victim"), Arg(args, "--loaded"), o) == 0 ? 0 : 1; }
                    catch (Exception ex) { o.WriteLine("FAIL  ui: exception " + ex); }
                }
                app.Shutdown(code);
            };
            app.Run(win);
            return code;
        }

        [STAThread]
        public static int Main(string[] args)
        {
            int si = Array.IndexOf(args, "--screenshot");
            if (si >= 0 && si + 1 < args.Length) return Screenshot(args, args[si + 1]);
            if (Array.IndexOf(args, "--uitest") >= 0 && Arg(args, "--victim") != null) return UiTest(args);
            int? cli = Cli.Start(args);
            if (cli.HasValue && cli.Value >= 0) return cli.Value;
            if (cli.HasValue)
            {
                MessageBox.Show(Cli.NeedAdmin, "ProfileJanitor", MessageBoxButton.OK, MessageBoxImage.Error);
                return 1;
            }
            var app = new App();
            app.DispatcherUnhandledException += (s, e) =>
            {
                try { File.WriteAllText(CrashLog, DateTime.Now + "\r\n" + e.Exception); } catch (Exception) { }
                MessageBox.Show(e.Exception.Message + "\n\n" + CrashLog, "ProfileJanitor", MessageBoxButton.OK, MessageBoxImage.Error);
                e.Handled = true;
                app.Shutdown(3);
            };
            app.InitializeComponent();
            return app.Run(new MainWindow());
        }
    }
}
