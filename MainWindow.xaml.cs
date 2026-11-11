using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Wpf.Ui.Appearance;

namespace ProfileJanitor
{
    // Wiersz tabeli: opakowanie ProfileInfo z powiadamianiem o zmianach (zaznaczenie, rozmiary, jezyk).
    class Row : INotifyPropertyChanged
    {
        public readonly ProfileInfo P;
        readonly Func<bool> allowLogoff;
        bool isChecked;
        public bool SizeDone;

        public Row(ProfileInfo p, Func<bool> allowLogoff) { P = p; this.allowLogoff = allowLogoff; }
        public event PropertyChangedEventHandler PropertyChanged;
        public void Refresh()
        {
            if (!CanCheck) isChecked = false;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
        }

        public bool CanCheck => P.CanDelete(allowLogoff());
        public bool IsChecked
        {
            get => isChecked;
            set { isChecked = value && CanCheck; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked))); }
        }

        // Konto bez rozpoznanej nazwy pokazujemy pod nazwa folderu - "(nieznane konto)" nic operatorowi nie mowi.
        bool Named => !P.Account.StartsWith("(");
        public string Title => Named ? P.Account : P.Path != "" ? P.Leaf : P.RegKey;
        public string Subtitle => P.Path != "" ? P.Path : P.RegKey;
        public string KindText => P.KindText;
        public string StateText => P.StateText;
        public string StateTip => P.StateText == "" ? null : P.StateText;

        // Rola: im wazniejsze konto, tym cieplejszy kolor - czerwony to wbudowany administrator, zielony to zwykly uzytkownik.
        public string RoleText => P.RoleText;
        public int RoleRank => (int)P.Role;
        public string RoleTone => P.Role == Role.BuiltinAdmin ? "danger" : P.Role == Role.Admin ? "warn" : P.Role == Role.User ? "ok" : P.Role == Role.Guest ? "neutral" : "none";
        public string RoleHint
        {
            get
            {
                switch (P.Role)
                {
                    case Role.BuiltinAdmin: return L.T("Wbudowane konto administratora (numer 500). Zaznaczanie wg wieku domyślnie je pomija.", "The built-in administrator account (RID 500). Selecting by age skips it by default.");
                    case Role.Admin: return L.T("Ma uprawnienia administratora na tym komputerze (bezpośrednio lub przez grupę). Zaznaczanie wg wieku domyślnie je pomija.", "Has administrator rights on this computer (directly or through a group). Selecting by age skips it by default.");
                    case Role.User: return L.T("Zwykłe konto bez uprawnień administratora na tym komputerze.", "A standard account without administrator rights on this computer.");
                    case Role.Guest: return L.T("Wbudowane konto gościa.", "The built-in guest account.");
                    default: return L.T("Roli nie da się ustalić: konto nie istnieje albo kontroler domeny jest niedostępny.", "The role cannot be determined: the account does not exist or the domain controller is unreachable.");
                }
            }
        }
        public DateTime? Last => P.LastLogon;
        public double Size => P.SizeBytes;

        // neutral = zwykle konto; pozostale kolory oznaczaja pozycje wymagajace uwagi
        public string Tone => P.Leftover != Leftover.None ? "info" : P.IsOrphan ? "warn" : P.Kind == Kind.UnknownNoDc ? "danger" : "neutral";
        public string KindHint
        {
            get
            {
                switch (P.Leftover)
                {
                    case Leftover.BakKey: return L.T("Zapasowy klucz rejestru po nieudanym logowaniu. Powoduje logowanie na profil tymczasowy.", "Backup registry key left by a failed logon. It causes temporary-profile logons.");
                    case Leftover.NoFolder: return L.T("Wpis w rejestrze wskazuje na folder, którego już nie ma.", "The registry entry points to a folder that no longer exists.");
                    case Leftover.NoRegistry: return L.T("Folder w katalogu profili, o którym system nic nie wie. Przez niego nowy profil dostaje nazwę z dopiskiem.", "A folder in the profiles directory that Windows does not know about. It makes new profiles get a suffixed name.");
                }
                switch (P.Kind)
                {
                    case Kind.Local: return L.T("Konto lokalne tego komputera.", "A local account of this computer.");
                    case Kind.Domain: return L.T("Konto domenowe, istnieje w domenie.", "A domain account that exists in the domain.");
                    case Kind.OrphanLocal:
                    case Kind.OrphanDomain: return L.T("Konto już nie istnieje - został sam profil.", "The account no longer exists - only the profile is left.");
                    default: return L.T("Nie udało się sprawdzić, czy konto istnieje (kontroler domeny niedostępny). Zaznacz ręcznie tylko wtedy, gdy masz pewność.", "Could not check whether the account exists (domain controller unreachable). Select manually only if you are sure.");
                }
            }
        }

        public string LastRel
        {
            get
            {
                if (!P.AgeDays.HasValue) return L.T("brak danych", "no data");
                int d = P.AgeDays.Value;
                if (d <= 0) return L.T("dzisiaj", "today");
                if (d == 1) return L.T("wczoraj", "yesterday");
                return L.T(d + " dni temu", d + " days ago");
            }
        }
        public string LastText => P.LastLogon.HasValue ? P.LastLogon.Value.ToString("yyyy-MM-dd HH:mm") : "";

        public static string Human(long bytes)
        {
            if (bytes < 0) return "-";
            double mb = bytes / 1048576.0;
            if (mb < 1) return bytes == 0 ? "0 MB" : "< 1 MB";
            return mb < 1024 ? mb.ToString("N0") + " MB" : (mb / 1024).ToString("N1") + " GB";
        }
        public string SizeText => SizeDone ? Human(P.SizeBytes) : "...";

        public string DetailsText
        {
            get
            {
                var sb = new StringBuilder(KindHint);
                if (P.Role != Role.Unknown) sb.Append("\n" + L.T("Rola: ", "Role: ") + P.RoleText + " - " + RoleHint);
                if (P.Path != "") sb.Append("\nFolder: " + P.Path);
                if (P.RegKey != "") sb.Append("\n" + L.T("Klucz rejestru: ", "Registry key: ") + P.RegKey);
                if (SizeDone && P.SizeBytes >= 0)
                    sb.Append("\n" + L.T("Pulpit: ", "Desktop: ") + Human(P.Desktop) + "   " + L.T("Dokumenty: ", "Documents: ") + Human(P.Documents) + "   " + L.T("Pobrane: ", "Downloads: ") + Human(P.Downloads));
                return sb.ToString();
            }
        }
        public string OpenLabel => L.T("Otwórz folder", "Open folder");
        public string ExcludeLabel => P.Excluded ? L.T("Na liście wykluczeń", "On the exclusion list") : L.T("Nigdy nie usuwaj (dodaj do wykluczeń)", "Never delete (add to exclusions)");
        public bool CanExclude => !P.Excluded;
    }

    public partial class MainWindow
    {
        static readonly Brush Danger = new SolidColorBrush(Color.FromRgb(0xE5, 0x48, 0x4D));
        readonly ObservableCollection<Row> rows = new ObservableCollection<Row>();
        readonly ICollectionView view;
        Target target;
        Snapshot snap = new Snapshot();
        bool busy;
        int generation;
        readonly TaskCompletionSource<bool> ready = new TaskCompletionSource<bool>();

        // Dla trybu --screenshot: pierwsze wczytanie (lacznie z rozmiarami) zakonczone / przykladowe zaznaczenie.
        public Task Ready { get { return ready.Task; } }
        public void DemoSelect()
        {
            SelectWhere(p => Core.ShouldSelect(p, 90, false, true));
            if (rows.Count > 1) Table.SelectedIndex = 1; // rozwija szczegoly drugiego wiersza
        }

        public MainWindow()
        {
            InitializeComponent();
            SystemThemeWatcher.Watch(this); // motyw jasny/ciemny jak w systemie
            Table.ItemsSource = rows;
            view = CollectionViewSource.GetDefaultView(rows);
            view.Filter = o => Matches((Row)o);
            Confirm = async q => await Box(q, L.T("Usuń", "Delete"), L.T("Anuluj", "Cancel"), true) == Wpf.Ui.Controls.MessageBoxResult.Primary;
            Notify = m => Box(m, null, "OK", false);
            ApplyTexts();
            PreviewKeyDown += (s, e) => { if (e.Key == Key.F1) { e.Handled = true; Help_Click(null, null); } };
            Loaded += (s, e) => Reload();
        }

        void ApplyTexts()
        {
            TxtHost.PlaceholderText = L.T("Ten komputer", "This computer");
            TxtHost.ToolTip = L.T("Nazwa innego komputera w sieci; puste pole oznacza ten komputer.", "Name of another computer on the network; empty means this computer.");
            BtnConnect.Content = L.T("Odśwież", "Refresh");
            TxtFilter.PlaceholderText = L.T("Szukaj konta, folderu, typu...", "Search account, folder, type...");
            BtnExclusions.Content = L.T("Wykluczenia", "Exclusions");
            BtnExclusions.ToolTip = L.T("Lista kont, których program nigdy nie pozwoli usunąć.", "Accounts the program will never let you delete.");
            BtnCsv.Content = "CSV";
            BtnCsv.ToolTip = L.T("Zapisz widoczną listę do pliku CSV.", "Save the visible list to a CSV file.");
            BtnLang.Content = L.En ? "EN" : "PL";
            BtnLang.ToolTip = "Język / Language";
            BtnTheme.ToolTip = L.T("Motyw jasny / ciemny", "Light / dark theme");
            BtnHelp.ToolTip = L.T("Pomoc (F1)", "Help (F1)");
            LblSelect.Text = L.T("Zaznacz:", "Select:");
            BtnByAge.Content = L.T("Nieużywane od", "Unused for");
            BtnByAge.ToolTip = L.T("Zaznacza profile, na które nikt się nie logował od podanej liczby dni.", "Selects profiles nobody has logged on to for the given number of days.");
            LblDays.Text = L.T("dni", "days");
            ChkSkipLocal.Content = L.T("bez lokalnych", "skip local");
            ChkSkipAdmins.Content = L.T("bez administratorów", "skip administrators");
            BtnOrphans.Content = L.T("Osierocone", "Orphaned");
            BtnOrphans.ToolTip = L.T("Profile kont, które już nie istnieją.", "Profiles of accounts that no longer exist.");
            BtnLeftovers.Content = L.T("Pozostałości", "Leftovers");
            BtnLeftovers.ToolTip = L.T("Śmieci po profilach: klucze .bak, foldery bez wpisu w rejestrze, wpisy bez folderu.", "Profile debris: .bak keys, folders without a registry entry, entries without a folder.");
            BtnNone.Content = L.T("Wyczyść", "Clear");
            ChkLogoff.Content = L.T("Wyloguj zalogowanych", "Log off signed-in users");
            ChkLogoff.ToolTip = L.T("Pozwala zaznaczyć profile użytkowników, którzy są teraz zalogowani. Zostaną wylogowani i stracą niezapisaną pracę.",
                "Lets you select profiles of users who are signed in now. They will be logged off and lose unsaved work.");
            ChkBackup.Content = L.T("Kopia ZIP do:", "ZIP backup to:");
            ChkBackup.ToolTip = L.T("Przed usunięciem pakuje profil do archiwum. Jeśli kopia się nie uda, profil nie zostanie usunięty.",
                "Zips the profile before deletion. If the backup fails, the profile is not deleted.");
            TxtBackup.PlaceholderText = L.T("Folder na kopie", "Backup folder");
            BtnBrowse.ToolTip = L.T("Wybierz folder", "Choose folder");
            ColDel.Header = "";
            ColAccount.Header = L.T("Konto", "Account");
            ColKind.Header = L.T("Typ", "Type");
            ColRole.Header = L.T("Rola", "Role");
            ColLast.Header = L.T("Ostatnie logowanie", "Last logon");
            ColSize.Header = L.T("Rozmiar", "Size");
            ColState.Header = L.T("Stan", "State");
            LblLoading.Text = L.T("Wczytywanie profili...", "Loading profiles...");
            foreach (var r in rows) r.Refresh();
            UpdateSummary();
        }

        Task<Wpf.Ui.Controls.MessageBoxResult> Box(string text, string primary, string close, bool danger)
        {
            var box = new Wpf.Ui.Controls.MessageBox
            {
                Title = "ProfileJanitor",
                Content = new ScrollViewer { MaxHeight = 460, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap } },
                CloseButtonText = close,
                MaxWidth = 900,
                Owner = this
            };
            if (primary != null)
            {
                box.PrimaryButtonText = primary;
                box.PrimaryButtonAppearance = danger ? Wpf.Ui.Controls.ControlAppearance.Danger : Wpf.Ui.Controls.ControlAppearance.Primary;
            }
            return box.ShowDialogAsync();
        }
        Task Info(string text) { return Notify(text); }

        // Pytanie o potwierdzenie i komunikat sa wymienialne, zeby test okna (--uitest) mogl odpowiadac bez klikania.
        Func<string, Task<bool>> Confirm;
        Func<string, Task> Notify;

        // Test okna od srodka: wywoluje te same procedury co klikniecia i sprawdza skutki. Zwraca liczbe bledow.
        // victimSid: profil testowy, ktory zostanie NAPRAWDE usuniety. loadedSid: profil zaladowany bez sesji (nie moze zniknac).
        public async Task<int> UiTestAsync(string victimSid, string loadedSid, TextWriter o)
        {
            int fail = 0;
            Action<string, bool> check = (name, ok) => { o.WriteLine((ok ? "PASS  ui: " : "FAIL  ui: ") + name); if (!ok) fail++; };
            await Ready;
            string question = "", report = "";
            bool answer = false;
            int asked = 0;
            Confirm = q => { asked++; question = q; return Task.FromResult(answer); };
            Notify = m => { report = m; return Task.FromResult(0); };
            Func<int> visible = () => view.Cast<object>().Count();

            check("rows loaded", rows.Count > 0 && rows.Count == snap.Profiles.Count);
            check("nothing selected at start", !rows.Any(r => r.IsChecked) && !BtnDelete.IsEnabled);

            NumDays.Value = 0; ChkSkipLocal.IsChecked = false; ChkSkipAdmins.IsChecked = false; ByAge_Click(null, null);
            check("select by age matches the rule", rows.Any(r => r.IsChecked) && rows.All(r => r.IsChecked == Core.ShouldSelect(r.P, 0, false, false)));
            ChkSkipAdmins.IsChecked = true; ByAge_Click(null, null);
            check("skip-administrators leaves administrators alone", rows.Any(r => r.P.IsAdmin) && !rows.Any(r => r.IsChecked && r.P.IsAdmin));
            ChkSkipAdmins.IsChecked = false; ByAge_Click(null, null);
            check("in-use, unverified and excluded rows never selected", !rows.Any(r => r.IsChecked && (r.P.Loaded || r.P.Excluded || r.P.IsOperator || r.P.Kind == Kind.UnknownNoDc)));
            check("delete button shows the count", BtnDelete.IsEnabled && ((string)BtnDelete.Content).Contains("(" + rows.Count(r => r.IsChecked) + ")"));
            ChkSkipLocal.IsChecked = true; ByAge_Click(null, null);
            check("skip-local leaves local accounts alone", !rows.Any(r => r.IsChecked && r.P.IsLocal));
            ByAge_Click(null, null); None_Click(null, null);
            check("clear removes every tick", !rows.Any(r => r.IsChecked) && !BtnDelete.IsEnabled);

            ChkSkipLocal.IsChecked = false; ByAge_Click(null, null);
            TxtFilter.Text = "zz-no-such-profile";
            check("filter hides rows and unticks them", visible() == 0 && !rows.Any(r => r.IsChecked) && !BtnDelete.IsEnabled && LblEmpty.Visibility == Visibility.Visible);
            TxtFilter.Text = "";
            check("clearing the filter brings rows back", visible() == rows.Count && LblEmpty.Visibility != Visibility.Visible);

            var victim = rows.FirstOrDefault(r => r.P.Sid == victimSid);
            check("victim row present and selectable", victim != null && victim.CanCheck);
            if (victim == null) return fail + 1;
            string path = victim.P.Path;
            victim.IsChecked = true; answer = false;
            await DeleteAsync();
            check("answering No deletes nothing", asked == 1 && question.Contains(path) && Directory.Exists(path));

            if (loadedSid != null)
            {
                var lr = rows.FirstOrDefault(r => r.P.Sid == loadedSid);
                check("in-use row is locked", lr != null && lr.P.Loaded && !lr.CanCheck);
                if (lr != null)
                {
                    lr.IsChecked = true;
                    check("in-use row cannot be ticked", !lr.IsChecked);
                    ChkLogoff.IsChecked = true;
                    check("logoff option unlocks the in-use row", lr.CanCheck);
                    foreach (var r in rows) r.IsChecked = false;
                    lr.IsChecked = true;
                    // Bezpiecznik testu: z wlaczona opcja wylogowania wolno usuwac WYLACZNIE profil testowy.
                    bool onlyTest = rows.Count(r => r.IsChecked) == 1 && lr.IsChecked;
                    check("only the test profile is ticked before the logoff test", onlyTest);
                    if (onlyTest)
                    {
                        string lpath = lr.P.Path;
                        answer = true;
                        await DeleteAsync();
                        check("confirmation warns about logging users off", question.Contains("WYLOGOWANI") || question.Contains("LOGGED OFF"));
                        check("in-use profile without a session is reported and kept", Directory.Exists(lpath) && (report.Contains("BŁĄD") || report.Contains("ERROR")));
                    }
                    ChkLogoff.IsChecked = false;
                    check("switching the option off locks in-use rows again", !rows.Any(r => r.P.Loaded && (r.CanCheck || r.IsChecked)));
                }
            }

            victim = rows.FirstOrDefault(r => r.P.Sid == victimSid);
            foreach (var r in rows) r.IsChecked = false;
            if (victim != null) victim.IsChecked = true;
            bool onlyVictim = victim != null && rows.Count(r => r.IsChecked) == 1;
            check("only the victim is ticked before the real deletion", onlyVictim);
            if (onlyVictim)
            {
                answer = true;
                await DeleteAsync();
                check("answering Yes deletes exactly the victim", !Directory.Exists(path) && !rows.Any(r => r.P.Sid == victimSid) && (report.Contains("USUNIĘTO") || report.Contains("DELETED")));
                check("report shows no errors", !report.Contains("BŁĄD (") && !report.Contains("ERROR ("));
            }

            bool en = L.En;
            string label = (string)BtnConnect.Content;
            await LangAsync();
            check("language switch changes the texts", L.En != en && (string)BtnConnect.Content != label && rows.Count > 0);
            await LangAsync();
            check("language switch back restores them", L.En == en && (string)BtnConnect.Content == label);
            var theme = ApplicationThemeManager.GetAppTheme();
            Theme_Click(null, null);
            check("theme button toggles the theme", ApplicationThemeManager.GetAppTheme() != theme);
            return fail;
        }

        bool Matches(Row r)
        {
            string f = TxtFilter.Text.Trim();
            var p = r.P;
            return f == "" || (p.Account + "\n" + p.Path + "\n" + p.RegKey + "\n" + p.KindText + "\n" + p.RoleText + "\n" + p.StateText).IndexOf(f, StringComparison.CurrentCultureIgnoreCase) >= 0;
        }

        void SetBusy(bool b)
        {
            busy = b;
            Header.IsEnabled = SelectBar.IsEnabled = ActionBar.IsEnabled = Table.IsEnabled = !b;
            Loading.Visibility = b ? Visibility.Visible : Visibility.Collapsed;
            UpdateSummary();
        }

        void ShowStatus(string text, Wpf.Ui.Controls.InfoBarSeverity severity, bool open)
        {
            Status.Severity = severity;
            Status.Title = severity == Wpf.Ui.Controls.InfoBarSeverity.Error ? L.T("Błąd", "Error") : L.T("Uwaga", "Note");
            Status.Message = text;
            Status.IsOpen = open;
        }

        async void Reload() { await ReloadAsync(); }

        async Task ReloadAsync()
        {
            int gen = ++generation;
            rows.Clear();
            Status.IsOpen = false;
            SetBusy(true);
            string host = TxtHost.Text;
            Target t = null;
            Snapshot s = null;
            string error = null;
            try { await Task.Run(() => { t = new Target(host); s = Core.Load(t, Core.LoadExclusions()); }); }
            catch (Exception e) { error = e.Message; }
            if (gen != generation) return;
            if (error != null)
            {
                target = null;
                snap = new Snapshot();
                ShowStatus(L.T("Nie udało się wczytać profili", "Could not load profiles") + (host.Trim() == "" ? "" : L.T(" z komputera ", " from computer ") + host.Trim()) + ": " + error,
                    Wpf.Ui.Controls.InfoBarSeverity.Error, true);
                SetBusy(false);
                ready.TrySetResult(true);
                return;
            }
            target = t;
            snap = s;
            Title = TitleBarCtl.Title = "ProfileJanitor  -  " + t.Host;
            ShowStatus(s.Status, Wpf.Ui.Controls.InfoBarSeverity.Warning, s.Warn); // gdy wszystko w porzadku, pasek nie zabiera miejsca
            foreach (var p in s.Profiles)
            {
                var r = new Row(p, () => ChkLogoff.IsChecked == true);
                r.PropertyChanged += (o, e) => { if (e.PropertyName == null || e.PropertyName == nameof(Row.IsChecked)) UpdateSummary(); };
                rows.Add(r);
            }
            SetBusy(false);

            foreach (var r in rows.ToList()) // rozmiary doliczaja sie w tle, okno jest juz uzywalne
            {
                var p = r.P;
                string fs = p.Path == "" ? "" : t.Fs(p.Path);
                long[] z = await Task.Run(() => fs == "" ? new long[] { -1, -1, -1, -1 } :
                    new[] { Core.DirSize(fs), Core.DirSize(fs + @"\Desktop"), Core.DirSize(fs + @"\Documents"), Core.DirSize(fs + @"\Downloads") });
                if (gen != generation) return;
                p.SizeBytes = z[0]; p.Desktop = z[1]; p.Documents = z[2]; p.Downloads = z[3];
                r.SizeDone = true;
                r.Refresh();
            }
            ready.TrySetResult(true);
        }

        void SelectWhere(Func<ProfileInfo, bool> pred)
        {
            foreach (Row r in view) r.IsChecked = pred(r.P); // tylko widoczne wiersze; setter sam odrzuca niedozwolone
            UpdateSummary();
        }

        // Kafelki u gory i przycisk usuwania: jedyne miejsca, w ktorych operator czyta liczby.
        void UpdateSummary()
        {
            if (T1Val == null || view == null) return;
            var all = rows.Select(r => r.P).ToList();
            var sel = rows.Where(r => r.IsChecked).Select(r => r.P).ToList();
            int visible = view.Cast<object>().Count();

            T1Cap.Text = L.T("Profile na komputerze", "Profiles on the computer");
            T1Val.Text = all.Count.ToString();
            T1Sub.Text = visible != all.Count ? L.T("widocznych po filtrze: " + visible, "visible after filter: " + visible)
                : L.T("w użyciu: " + all.Count(p => p.Loaded), "in use: " + all.Count(p => p.Loaded));

            T2Cap.Text = L.T("Do odzyskania", "Space to reclaim");
            T2Val.Text = sel.Count == 0 ? "0 MB" : Row.Human(sel.Where(p => p.SizeBytes > 0).Sum(p => p.SizeBytes));
            T2Sub.Text = L.T("zaznaczonych pozycji: " + sel.Count, "items selected: " + sel.Count);

            T3Cap.Text = L.T("Wolne miejsce na ", "Free space on ") + (snap.Drive == "" ? "-" : snap.Drive);
            T3Val.Text = Row.Human(snap.FreeBytes);
            bool low = snap.TotalBytes > 0 && snap.FreeBytes < snap.TotalBytes / 10;
            if (low) T3Val.Foreground = Danger;
            else T3Val.ClearValue(TextBlock.ForegroundProperty); // wraca kolor ze stylu (zalezny od motywu)
            T3Bar.Value = snap.TotalBytes > 0 ? 100.0 * (snap.TotalBytes - snap.FreeBytes) / snap.TotalBytes : 0;
            T3Sub.Text = snap.TotalBytes > 0 ? L.T("z ", "of ") + Row.Human(snap.TotalBytes) + (low ? L.T("  -  dysk prawie pełny", "  -  disk almost full") : "") : "";

            int unverified = all.Count(p => p.Leftover == Leftover.None && p.Kind == Kind.UnknownNoDc), orphans = all.Count(p => p.IsOrphan), leftovers = all.Count(p => p.Leftover != Leftover.None);
            T4Cap.Text = L.T("Wymagają uwagi", "Need attention");
            T4Val.Text = (unverified + orphans + leftovers).ToString();
            var parts = new List<string>(); // tylko niezerowe kategorie - kafelek jest waski
            if (orphans > 0) parts.Add(L.T("osierocone: ", "orphaned: ") + orphans);
            if (leftovers > 0) parts.Add(L.T("pozostałości: ", "leftovers: ") + leftovers);
            if (unverified > 0) parts.Add(L.T("niezweryfikowane: ", "unverified: ") + unverified);
            T4Sub.Text = parts.Count == 0 ? L.T("nic do sprawdzenia", "nothing to review") : string.Join("   ", parts);

            BtnDelete.Content = sel.Count == 0 ? L.T("Usuń zaznaczone", "Delete selected") : L.T("Usuń zaznaczone (" + sel.Count + ")", "Delete selected (" + sel.Count + ")");
            BtnDelete.IsEnabled = !busy && sel.Count > 0;
            LblEmpty.Text = all.Count == 0 ? L.T("Brak profili do pokazania.", "No profiles to show.") : L.T("Nic nie pasuje do wyszukiwania.", "Nothing matches the search.");
            LblEmpty.Visibility = !busy && visible == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        void Connect_Click(object sender, RoutedEventArgs e) { Reload(); }
        void TxtHost_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { e.Handled = true; Reload(); } }
        void ByAge_Click(object sender, RoutedEventArgs e) { int d = (int)(NumDays.Value ?? 0); bool local = ChkSkipLocal.IsChecked == true, admins = ChkSkipAdmins.IsChecked == true; SelectWhere(p => Core.ShouldSelect(p, d, local, admins)); }
        void Orphans_Click(object sender, RoutedEventArgs e) { SelectWhere(p => p.IsOrphan); }
        void Leftovers_Click(object sender, RoutedEventArgs e) { SelectWhere(p => p.Leftover != Leftover.None); }
        void None_Click(object sender, RoutedEventArgs e) { SelectWhere(p => false); }
        void Logoff_Changed(object sender, RoutedEventArgs e) { foreach (var r in rows) r.Refresh(); UpdateSummary(); }
        async void Lang_Click(object sender, RoutedEventArgs e) { await LangAsync(); }
        async Task LangAsync() { L.En = !L.En; L.Save(); ApplyTexts(); await ReloadAsync(); }

        void Help_Click(object sender, RoutedEventArgs e) { Help.Create(this).Show(); }

        void Theme_Click(object sender, RoutedEventArgs e)
        {
            ApplicationThemeManager.Apply(ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark ? ApplicationTheme.Light : ApplicationTheme.Dark);
        }

        void TxtFilter_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (view == null) return;
            foreach (var r in rows) if (!Matches(r)) r.IsChecked = false; // niewidoczny wiersz nie moze zostac zaznaczony do usuniecia
            view.Refresh();
            UpdateSummary();
        }

        void Browse_Click(object sender, RoutedEventArgs e)
        {
            using (var d = new System.Windows.Forms.FolderBrowserDialog { Description = L.T("Folder na kopie profili", "Folder for profile backups") })
                if (d.ShowDialog() == System.Windows.Forms.DialogResult.OK) { TxtBackup.Text = d.SelectedPath; ChkBackup.IsChecked = true; }
        }

        async void OpenFolder(ProfileInfo p)
        {
            if (target == null || p.Path == "") return;
            string fs = target.Fs(p.Path);
            if (Directory.Exists(fs)) Process.Start("explorer.exe", "\"" + fs + "\"");
            else await Info(L.T("Folder nie istnieje lub jest niedostępny:\n", "The folder does not exist or is not accessible:\n") + fs);
        }

        void OpenFolder_Click(object sender, RoutedEventArgs e) { OpenFolder(((Row)((FrameworkElement)sender).DataContext).P); }

        void Table_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            // tylko dwuklik w komorke wiersza: nie w naglowek, nie w pole wyboru i nie w rozwiniete szczegoly
            var el = e.OriginalSource as DependencyObject;
            DataGridCell cell = null;
            while (el != null && !(el is DataGridRow))
            {
                if (el is DataGridCell) cell = (DataGridCell)el;
                el = el is Visual ? VisualTreeHelper.GetParent(el) : LogicalTreeHelper.GetParent(el);
            }
            var row = el as DataGridRow;
            if (row != null && cell != null && cell.Column != ColDel) OpenFolder(((Row)row.Item).P);
        }

        // Dopisuje konto do listy wykluczen bez wysylania operatora do Notatnika.
        async void Exclude_Click(object sender, RoutedEventArgs e)
        {
            var p = ((Row)((FrameworkElement)sender).DataContext).P;
            try { File.AppendAllText(Core.ExclusionFile, "\r\n# " + p.Account + "  " + p.Path + "\r\n" + (p.Sid != "" ? p.Sid : p.Leaf) + "\r\n", Encoding.UTF8); }
            catch (Exception ex)
            {
                await Info(L.T("Nie udało się zapisać listy wykluczeń:\n", "Could not save the exclusion list:\n") + Core.ExclusionFile + "\n" + ex.Message);
                return;
            }
            Reload();
        }

        async void Exclusions_Click(object sender, RoutedEventArgs e)
        {
            string file = Core.ExclusionFile;
            try
            {
                // Naglowek pliku jest dwujezyczny: plik zostaje na dysku, a jezyk okna mozna potem zmienic.
                // Stary, jednojezyczny naglowek podmieniamy tylko wtedy, gdy plik nie ma jeszcze zadnych wpisow.
                bool oldHeader = File.Exists(file) && Core.LoadExclusions().Count == 0 && File.ReadAllText(file).TrimStart().StartsWith("# Profil");
                if (!File.Exists(file) || oldHeader)
                    File.WriteAllText(file,
                        "# ProfileJanitor - wykluczenia / exclusions\r\n" +
                        "# PL: profili z tej listy nie da się zaznaczyć ani usunąć. Jeden wpis w wierszu: nazwa konta (np. Admin albo DOMENA\\jan), SID albo nazwa folderu profilu.\r\n" +
                        "# EN: profiles listed here can never be selected or deleted. One entry per line: account name (e.g. Admin or DOMAIN\\john), SID or profile folder name.\r\n", Encoding.UTF8);
                var proc = Process.Start("notepad.exe", "\"" + file + "\"");
                await Task.Run(() => proc.WaitForExit());
            }
            catch (Exception ex)
            {
                await Info(L.T("Nie udało się otworzyć listy wykluczeń:\n", "Could not open the exclusion list:\n") + file + "\n" + ex.Message);
                return;
            }
            Reload();
        }

        async void Csv_Click(object sender, RoutedEventArgs e)
        {
            if (target == null) return;
            var d = new Microsoft.Win32.SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = "profiles_" + target.Host + "_" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".csv" };
            if (d.ShowDialog(this) != true) return;
            try { File.WriteAllText(d.FileName, Core.Csv(view.Cast<Row>().Select(r => r.P), target.Host), new UTF8Encoding(true)); } // BOM, zeby Excel poznal UTF-8
            catch (Exception ex) { await Info(L.T("Nie udało się zapisać pliku:\n", "Could not save the file:\n") + ex.Message); }
        }

        async void Delete_Click(object sender, RoutedEventArgs e) { await DeleteAsync(); }

        async Task DeleteAsync()
        {
            bool logoff = ChkLogoff.IsChecked == true;
            var sel = rows.Where(r => r.IsChecked).Select(r => r.P).Where(p => p.CanDelete(logoff)).ToList();
            if (sel.Count == 0 || target == null) return;
            string backupDir = ChkBackup.IsChecked == true ? TxtBackup.Text.Trim() : null;
            if (backupDir == "")
            {
                await Info(L.T("Zaznaczono kopię ZIP, ale nie podano folderu na kopie.", "ZIP backup is ticked, but no backup folder was given."));
                return;
            }
            var names = sel.Take(20).Select(p => "  " + p.Account + "  -  " + p.Path + "  [" + p.KindText + "]").ToList();
            if (sel.Count > 20) names.Add(L.T("  ... i " + (sel.Count - 20) + " kolejnych", "  ... and " + (sel.Count - 20) + " more"));
            int toLogoff = sel.Count(p => p.Loaded);
            string question = L.T("Usunąć bezpowrotnie z komputera ", "Permanently delete from computer ") + target.Host + " (" + sel.Count + ")?\n\n" + string.Join("\n", names) +
                (toLogoff > 0 ? L.T("\n\nUWAGA: zalogowani użytkownicy (" + toLogoff + ") zostaną WYLOGOWANI, niezapisana praca przepadnie.",
                    "\n\nWARNING: signed-in users (" + toLogoff + ") will be LOGGED OFF and lose unsaved work.") : "") +
                (backupDir != null ? L.T("\n\nPrzed usunięciem kopia ZIP trafi do: ", "\n\nBefore deletion a ZIP backup goes to: ") + backupDir : L.T("\n\nBez kopii zapasowej.", "\n\nNo backup.")) +
                L.T("\n\nZniknie cały folder profilu wraz z plikami użytkownika. Tej operacji nie można cofnąć.",
                    "\n\nThe whole profile folder with the user's files will be gone. This cannot be undone.");
            if (!await Confirm(question)) return;

            generation++; // zatrzymuje liczenie rozmiarow w tle
            SetBusy(true);
            var t = target;
            int failed = 0;
            string deleting = L.T("Usuwanie: ", "Deleting: ");
            string report = await Task.Run(() => Core.DeleteMany(t, sel, logoff, backupDir, false,
                p => Dispatcher.BeginInvoke((Action)(() => LblLoading.Text = deleting + p.Path + " ...")), out failed));
            LblLoading.Text = L.T("Wczytywanie profili...", "Loading profiles...");
            await Info(report);
            await ReloadAsync();
        }
    }
}
