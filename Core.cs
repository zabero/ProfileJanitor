// ProfileJanitor (PJ) - przeglad i usuwanie profili uzytkownikow Windows, lokalnie i zdalnie.
// Ten plik: logika i tryb wiersza polecen. Okno programu jest w MainWindow.xaml(.cs), start w App.xaml.cs.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.DirectoryServices;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

[assembly: System.Reflection.AssemblyTitle("ProfileJanitor")]
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]
// Bez tego atrybutu .NET wlacza stara obsluge sciezek (limit 260 znakow), a profile maja glebokie drzewa.
[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8")]

// Jezyk interfejsu: kazdy tekst jest zapisany w miejscu uzycia w obu wersjach - L.T("po polsku", "in English").
static class L
{
    public static bool En;
    const string Key = @"Software\ProfileJanitor";

    // Kolejnosc: parametr --lang, wybor zapisany dla uzytkownika, jezyk systemu.
    public static void Init(string forced)
    {
        string v = forced;
        if (v == null)
            try { using (var k = Registry.CurrentUser.OpenSubKey(Key)) v = k == null ? null : k.GetValue("Language") as string; }
            catch (Exception) { }
        if (v == null) v = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        En = !string.Equals(v, "pl", StringComparison.OrdinalIgnoreCase);
    }
    public static void Save()
    {
        try { using (var k = Registry.CurrentUser.CreateSubKey(Key)) k.SetValue("Language", En ? "en" : "pl"); }
        catch (Exception) { }
    }
    public static string T(string pl, string en) { return En ? en : pl; }
}

enum Kind { Local, Domain, OrphanLocal, OrphanDomain, UnknownNoDc }
enum Leftover { None, BakKey, NoFolder, NoRegistry }
enum Role { Unknown, User, Guest, Admin, BuiltinAdmin } // rola konta NA TYM komputerze

class ProfileInfo
{
    public string Sid = "", RegKey = "", Path = "", Account = ""; // RegKey: nazwa klucza w ProfileList (SID lub SID.bak)
    public Kind Kind;
    public Leftover Leftover;
    public Role Role;
    public bool Loaded, Excluded, IsOperator;
    public DateTime? LastLogon;
    public long SizeBytes = -1, Desktop = -1, Documents = -1, Downloads = -1;

    public int? AgeDays
    {
        get { return LastLogon.HasValue ? (int?)(int)(DateTime.Now - LastLogon.Value).TotalDays : null; }
    }
    public bool IsLocal { get { return Leftover == Leftover.None && (Kind == Kind.Local || Kind == Kind.OrphanLocal); } }
    public bool IsOrphan { get { return Leftover == Leftover.None && (Kind == Kind.OrphanLocal || Kind == Kind.OrphanDomain); } }
    public bool IsAdmin { get { return Role == Role.Admin || Role == Role.BuiltinAdmin; } }
    public string RoleText
    {
        get
        {
            switch (Role)
            {
                case Role.BuiltinAdmin: return L.T("Admin wbudowany", "Built-in admin");
                case Role.Admin: return "Administrator";
                case Role.User: return L.T("Użytkownik", "User");
                case Role.Guest: return L.T("Gość", "Guest");
                default: return "";
            }
        }
    }
    public string Leaf { get { return System.IO.Path.GetFileName(Path.TrimEnd('\\')); } }
    public string Id { get { return RegKey != "" ? RegKey : Path; } }
    public bool CanDelete(bool allowLogoff) { return !Excluded && !IsOperator && (!Loaded || allowLogoff); }
    public string StateText
    {
        get
        {
            if (IsOperator) return L.T("Twoja sesja - nie można usunąć", "Your session - cannot be deleted");
            if (Excluded) return L.T("Wykluczony na stałe", "Permanently excluded");
            return Loaded ? L.T("W użyciu (zalogowany)", "In use (logged on)") : "";
        }
    }
    public string KindText
    {
        get
        {
            switch (Leftover)
            {
                case Leftover.BakKey: return L.T("Pozostałość: klucz .bak", "Leftover: .bak key");
                case Leftover.NoFolder: return L.T("Pozostałość: wpis bez folderu", "Leftover: entry without folder");
                case Leftover.NoRegistry: return L.T("Pozostałość: folder bez wpisu", "Leftover: folder without entry");
            }
            switch (Kind)
            {
                case Kind.Local: return L.T("Lokalne", "Local");
                case Kind.Domain: return L.T("Domenowe", "Domain");
                case Kind.OrphanLocal: return L.T("Osierocone (lokalne)", "Orphaned (local)");
                case Kind.OrphanDomain: return L.T("Osierocone (domena)", "Orphaned (domain)");
                default: return L.T("Niezweryfikowane", "Unverified");
            }
        }
    }
    public string Describe { get { return Account + "  " + Path + "  [" + KindText + (RoleText != "" ? ", " + RoleText : "") + (RegKey != "" ? ", " + RegKey : "") + "]"; } }
}

class Snapshot
{
    public List<ProfileInfo> Profiles = new List<ProfileInfo>();
    public string Status = "", Drive = "";
    public bool Warn;
    public long FreeBytes = -1, TotalBytes = -1;
}

// Rejestr przez WMI (StdRegProv): ta sama sciezka kodu lokalnie i zdalnie, bez uslugi Rejestr zdalny.
class Reg
{
    public const uint HKLM = 0x80000002, HKU = 0x80000003;
    readonly ManagementClass cls;

    public Reg(string host)
    {
        // shortcut: nie wymuszamy architektury dostawcy - 64-bitowy Windows domyslnie podaje 64-bitowy rejestr; do zmiany, gdyby operator pracowal z 32-bitowego systemu
        var scope = new ManagementScope(@"\\" + host + @"\root\default");
        scope.Connect();
        cls = new ManagementClass(scope, new ManagementPath("StdRegProv"), null);
    }

    ManagementBaseObject Call(string method, uint hive, string key, string value)
    {
        using (var inp = cls.GetMethodParameters(method))
        {
            inp["hDefKey"] = hive;
            inp["sSubKeyName"] = key;
            if (value != null) inp["sValueName"] = value;
            return cls.InvokeMethod(method, inp, null);
        }
    }
    static bool Ok(ManagementBaseObject r) { return Convert.ToUInt32(r["ReturnValue"]) == 0; }

    // null = klucz nie istnieje
    public string[] SubKeys(uint hive, string key)
    {
        var r = Call("EnumKey", hive, key, null);
        return Ok(r) ? ((string[])r["sNames"] ?? new string[0]) : null;
    }
    public string GetString(string key, string name)
    {
        var r = Call("GetExpandedStringValue", HKLM, key, name);
        if (!Ok(r)) r = Call("GetStringValue", HKLM, key, name);
        return Ok(r) ? (string)r["sValue"] : null;
    }
    public object GetDword(string key, string name)
    {
        var r = Call("GetDWORDValue", HKLM, key, name);
        return Ok(r) ? r["uValue"] : null;
    }
    public bool DeleteTree(string key)
    {
        foreach (string sub in SubKeys(HKLM, key) ?? new string[0]) DeleteTree(key + "\\" + sub);
        return Ok(Call("DeleteKey", HKLM, key, null));
    }
}

// Komputer, na ktorym pracujemy. Pusta nazwa, "." lub wlasna nazwa = lokalnie; wszystko inne idzie sciezka zdalna.
class Target
{
    public readonly string Host;
    public readonly bool IsLocal;
    public readonly Reg Reg;
    readonly ManagementScope cim;

    public Target(string host)
    {
        host = (host ?? "").Trim().TrimStart('\\');
        IsLocal = host == "" || host == "." || string.Equals(host, Environment.MachineName, StringComparison.OrdinalIgnoreCase);
        Host = IsLocal ? Environment.MachineName : host;
        cim = new ManagementScope(@"\\" + (IsLocal ? "." : Host) + @"\root\cimv2");
        cim.Connect();
        Reg = new Reg(IsLocal ? "." : Host);
    }

    public List<ManagementObject> Query(string wql)
    {
        using (var s = new ManagementObjectSearcher(cim, new ObjectQuery(wql)))
            return s.Get().Cast<ManagementObject>().ToList();
    }

    // Sciezka na dysku komputera docelowego -> sciezka osiagalna stad (zdalnie: udzial administracyjny).
    public string Fs(string path) { return FsPath(Host, IsLocal, path); }
    public static string FsPath(string host, bool isLocal, string path)
    {
        if (isLocal || path.Length < 2 || path[1] != ':') return path;
        return @"\\" + host + @"\" + path[0] + "$" + path.Substring(2);
    }
}

static class Wts
{
    [DllImport("wtsapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern IntPtr WTSOpenServer(string name);
    [DllImport("wtsapi32.dll")] static extern void WTSCloseServer(IntPtr h);
    [DllImport("wtsapi32.dll", SetLastError = true)] static extern bool WTSEnumerateSessions(IntPtr h, int reserved, int version, out IntPtr info, out int count);
    [DllImport("wtsapi32.dll")] static extern void WTSFreeMemory(IntPtr p);
    [DllImport("wtsapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern bool WTSQuerySessionInformation(IntPtr h, int session, int infoClass, out IntPtr buf, out int bytes);
    [DllImport("wtsapi32.dll", SetLastError = true)] static extern bool WTSLogoffSession(IntPtr h, int session, bool wait);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct SessionInfo { public int Id; public string Station; public int State; }

    static string Info(IntPtr h, int session, int infoClass)
    {
        IntPtr buf; int bytes;
        if (!WTSQuerySessionInformation(h, session, infoClass, out buf, out bytes)) return "";
        try { return Marshal.PtrToStringUni(buf) ?? ""; } finally { WTSFreeMemory(buf); }
    }

    // Wylogowuje wszystkie sesje konta "DOMENA\uzytkownik". Zwraca liczbe wylogowanych sesji.
    public static int Logoff(Target t, string account)
    {
        IntPtr h = t.IsLocal ? IntPtr.Zero : WTSOpenServer(t.Host);
        try
        {
            IntPtr list; int count;
            if (!WTSEnumerateSessions(h, 0, 1, out list, out count)) throw new Win32Exception(Marshal.GetLastWin32Error());
            int done = 0;
            try
            {
                int own = t.IsLocal ? Process.GetCurrentProcess().SessionId : -1;
                for (int i = 0; i < count; i++)
                {
                    var s = (SessionInfo)Marshal.PtrToStructure(new IntPtr(list.ToInt64() + (long)i * Marshal.SizeOf(typeof(SessionInfo))), typeof(SessionInfo));
                    string user = Info(h, s.Id, 5), domain = Info(h, s.Id, 7); // WTSUserName, WTSDomainName
                    if (user == "" || s.Id == own) continue;
                    if (!string.Equals(domain + "\\" + user, account, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!WTSLogoffSession(h, s.Id, true)) throw new Win32Exception(Marshal.GetLastWin32Error());
                    done++;
                }
            }
            finally { WTSFreeMemory(list); }
            return done;
        }
        finally { if (h != IntPtr.Zero) WTSCloseServer(h); }
    }
}

static class Core
{
    public const string PL = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList";
    const string PG = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileGuid";
    const string EventSource = "ProfileJanitor";
    static readonly HashSet<string> SystemFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Public", "Default", "Default User", "All Users" };

    public static string ExclusionFile
    {
        get { return Path.ChangeExtension(System.Reflection.Assembly.GetEntryAssembly().Location, ".exclude.txt"); }
    }

    public static HashSet<string> LoadExclusions()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (File.Exists(ExclusionFile))
            foreach (string line in File.ReadAllLines(ExclusionFile))
                if (line.Trim() != "" && !line.Trim().StartsWith("#")) set.Add(line.Trim());
        return set;
    }

    // Wpis na liscie wykluczen moze byc SID-em, nazwa konta (z domena lub bez) albo nazwa folderu profilu.
    public static bool IsExcluded(ProfileInfo p, HashSet<string> ex)
    {
        string user = p.Account.Substring(p.Account.IndexOf('\\') + 1);
        return (p.Sid != "" && ex.Contains(p.Sid)) || ex.Contains(p.Account) || ex.Contains(user) || ex.Contains(p.Leaf);
    }

    // Tylko konta uzytkownikow (lokalne/domenowe i Azure AD); profile uslug i pul IIS zostawiamy w spokoju.
    public static bool IsUserSid(string s)
    {
        if (!s.StartsWith("S-1-5-21-", StringComparison.OrdinalIgnoreCase) && !s.StartsWith("S-1-12-1-", StringComparison.OrdinalIgnoreCase)) return false;
        try { new SecurityIdentifier(s); return true; }
        catch (ArgumentException) { return false; }
    }

    public static DateTime? FileTimeFromParts(object high, object low)
    {
        if (high == null || low == null) return null;
        long ft = ((Convert.ToInt64(high) & 0xFFFFFFFFL) << 32) | (Convert.ToInt64(low) & 0xFFFFFFFFL);
        if (ft <= 0) return null;
        try { return DateTime.FromFileTime(ft); }
        catch (ArgumentOutOfRangeException) { return null; }
    }

    // Czas ostatniego zaladowania/wyladowania profilu wg rejestru (metoda Microsoftu; daty plikow sa niewiarygodne).
    static DateTime? LastLogon(Target t, string key)
    {
        var load = FileTimeFromParts(t.Reg.GetDword(key, "LocalProfileLoadTimeHigh"), t.Reg.GetDword(key, "LocalProfileLoadTimeLow"));
        var unload = FileTimeFromParts(t.Reg.GetDword(key, "LocalProfileUnloadTimeHigh"), t.Reg.GetDword(key, "LocalProfileUnloadTimeLow"));
        if (load == null) return unload;
        if (unload == null) return load;
        return load > unload ? load : unload;
    }

    public static Kind Classify(SecurityIdentifier sid, SecurityIdentifier machineSid, bool resolved, bool dcReachable)
    {
        bool local = machineSid != null && machineSid.Equals(sid.AccountDomainSid);
        if (local) return resolved ? Kind.Local : Kind.OrphanLocal;
        if (resolved) return Kind.Domain;
        // Bez kontrolera domeny nie da sie odroznic konta usunietego od chwilowo nieosiagalnego.
        return dcReachable ? Kind.OrphanDomain : Kind.UnknownNoDc;
    }

    // Zaznaczanie wg wieku: nigdy profili w uzyciu, wykluczonych, bez znanej daty, niezweryfikowanych ani pozostalosci.
    public static bool ShouldSelect(ProfileInfo p, int days, bool skipLocal, bool skipAdmins)
    {
        if (!p.CanDelete(false) || p.Leftover != Leftover.None || !p.AgeDays.HasValue || p.Kind == Kind.UnknownNoDc) return false;
        if (skipLocal && p.IsLocal) return false;
        if (skipAdmins && p.IsAdmin) return false;
        return p.AgeDays.Value >= days;
    }

    public static bool SelectAuto(ProfileInfo p, int days, bool skipLocal, bool skipAdmins, bool orphans, bool leftovers)
    {
        if (!p.CanDelete(false)) return false;
        if (p.Leftover != Leftover.None) return leftovers;
        return (orphans && p.IsOrphan) || ShouldSelect(p, days, skipLocal, skipAdmins);
    }

    // SID-y bezposrednich czlonkow lokalnej grupy Administratorzy (konta i grupy). null = nie udalo sie odczytac.
    static HashSet<string> AdminSids(Target t)
    {
        try
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "S-1-5-32-544" };
            foreach (var g in t.Query("SELECT Name FROM Win32_Group WHERE LocalAccount=True AND SID='S-1-5-32-544'")) // nazwa grupy zalezy od jezyka systemu
                using (var grp = new DirectoryEntry("WinNT://" + t.Host + "/" + (string)g["Name"] + ",group"))
                    foreach (object m in (System.Collections.IEnumerable)grp.Invoke("Members"))
                        using (var de = new DirectoryEntry(m))
                            set.Add(new SecurityIdentifier((byte[])de.Properties["objectSid"].Value, 0).Value);
            return set;
        }
        catch (Exception) { return null; }
    }

    // Konta o stalych numerach: 500 = wbudowany administrator, 501 = gosc (lokalne i domenowe).
    public static Role WellKnownRole(string sid)
    {
        if (!sid.StartsWith("S-1-5-21-", StringComparison.OrdinalIgnoreCase)) return Role.Unknown;
        return sid.EndsWith("-500") ? Role.BuiltinAdmin : sid.EndsWith("-501") ? Role.Guest : Role.Unknown;
    }

    // Rola z zebranych faktow. null = danego faktu nie udalo sie ustalic; bez zadnego faktu zwraca Unknown zamiast zgadywac.
    public static Role DecideRole(Role known, bool resolved, bool adminsKnown, bool direct, bool localAccount, bool? cachedAdmin, bool? ldapAdmin)
    {
        if (known == Role.BuiltinAdmin) return known;
        if (!resolved) return Role.Unknown;
        if (known != Role.Unknown) return known;
        if (!adminsKnown) return Role.Unknown;
        if (direct || cachedAdmin == true || ldapAdmin == true) return Role.Admin;
        if (localAccount || ldapAdmin == false || cachedAdmin == false) return Role.User;
        return Role.Unknown;
    }

    // Grupy konta zapamietane przez Windows przy jego ostatnim logowaniu na tym komputerze (null = brak wpisu).
    // Dziala bez kontrolera domeny i dla operatora bez konta domenowego - w przeciwienstwie do zapytania LDAP.
    static List<string> CachedGroups(Target t, string sid)
    {
        string key = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Group Policy\" + sid + @"\GroupMembership";
        object count = t.Reg.GetDword(key, "Count");
        if (count == null) return null;
        var list = new List<string>();
        for (int i = 0; i < Convert.ToInt32(count) && i < 2000; i++)
        {
            string g = t.Reg.GetString(key, "Group" + i);
            if (g != null) list.Add(g);
        }
        return list;
    }

    // Rola na tym komputerze: czlonkostwo bezposrednie, potem grupy z ostatniego logowania, na koncu zapytanie do domeny.
    static Role RoleOf(Target t, string sid, bool localAccount, bool resolved, bool dc, HashSet<string> admins)
    {
        Role known = WellKnownRole(sid);
        if (known != Role.Unknown || !resolved || admins == null) return DecideRole(known, resolved, admins != null, false, localAccount, null, null);
        bool direct = admins.Contains(sid);
        bool? cached = null, ldap = null;
        if (!direct && !localAccount)
        {
            var groups = CachedGroups(t, sid);
            if (groups != null) cached = groups.Any(admins.Contains);
            if (cached != true && dc)
                try
                {
                    // konto domenowe moze byc administratorem przez grupe (np. Domain Admins): tokenGroups = wszystkie jego grupy
                    using (var de = new DirectoryEntry("LDAP://<SID=" + sid + ">"))
                    {
                        de.RefreshCache(new[] { "tokenGroups" });
                        ldap = de.Properties["tokenGroups"].Cast<byte[]>().Any(g => admins.Contains(new SecurityIdentifier(g, 0).Value));
                    }
                }
                catch (Exception) { } // operator bez konta domenowego nie ma jak zapytac domeny
        }
        return DecideRole(known, resolved, true, direct, localAccount, cached, ldap);
    }

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)] static extern int DsGetDcName(string computer, string domain, IntPtr guid, string site, uint flags, out IntPtr info);
    [DllImport("netapi32.dll")] static extern int NetApiBufferFree(IntPtr buffer);

    // Lokalizator kontrolerow domeny. Nie wymaga konta domenowego (w przeciwienstwie do polaczenia LDAP), wiec dziala tez
    // wtedy, gdy program uruchamia lokalny administrator. Za kazdym razem szuka na nowo, nie ufa pamieci podrecznej.
    static bool DcReachable(string domain)
    {
        const uint ForceRediscovery = 0x1, DirectoryServiceRequired = 0x10, ReturnDnsName = 0x40000000;
        IntPtr info;
        if (DsGetDcName(null, domain, IntPtr.Zero, null, ForceRediscovery | DirectoryServiceRequired | ReturnDnsName, out info) != 0) return false;
        NetApiBufferFree(info);
        return true;
    }

    static string DomainStatus(Target t, out bool reachable, out bool warn)
    {
        reachable = false; warn = false;
        foreach (var o in t.Query("SELECT Domain, PartOfDomain FROM Win32_ComputerSystem"))
        {
            if (!(bool)o["PartOfDomain"]) return L.T("Komputer nie należy do domeny.", "The computer is not joined to a domain.");
            string d = (string)o["Domain"];
            if (DcReachable(d))
            {
                reachable = true;
                return L.T("Domena " + d + ": kontroler dostępny.", "Domain " + d + ": controller reachable.");
            }
            warn = true;
            return L.T("Domena " + d + ": kontroler niedostępny. Kont domenowych nie da się sprawdzić, dlatego mają typ \"Niezweryfikowane\" i nie są zaznaczane automatycznie.",
                "Domain " + d + ": controller unreachable. Domain accounts cannot be checked, so they are marked \"Unverified\" and are never selected automatically.");
        }
        return L.T("Nie udało się ustalić przynależności do domeny.", "Could not determine domain membership.");
    }

    public static Snapshot Load(Target t, HashSet<string> exclusions)
    {
        var snap = new Snapshot();
        bool dc;
        snap.Status = DomainStatus(t, out dc, out snap.Warn);

        var local = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var o in t.Query("SELECT Name, SID FROM Win32_UserAccount WHERE LocalAccount=True")) local[(string)o["SID"]] = (string)o["Name"];
        SecurityIdentifier machineSid = local.Count > 0 ? new SecurityIdentifier(local.Keys.First()).AccountDomainSid : null;
        string operatorSid = t.IsLocal ? WindowsIdentity.GetCurrent().User.Value : null;

        var admins = AdminSids(t);
        var loaded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var o in t.Query("SELECT SID, Loaded FROM Win32_UserProfile")) if ((bool)o["Loaded"]) loaded.Add((string)o["SID"]);

        string profilesDir = (t.Reg.GetString(PL, "ProfilesDirectory") ?? "").TrimEnd('\\');
        // Bez dostepu do dysku brak folderu nic nie znaczy - wtedy nie wolno zgadywac, co jest pozostaloscia.
        bool fsOk = profilesDir != "" && Directory.Exists(t.Fs(profilesDir));
        if (!fsOk)
        {
            snap.Warn = true;
            snap.Status += L.T("  Brak dostępu do dysku komputera (udział administracyjny): rozmiary i pozostałości po folderach niedostępne.",
                "  No access to the computer's disk (administrative share): sizes and folder leftovers unavailable.");
        }

        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string key in t.Reg.SubKeys(Reg.HKLM, PL) ?? new string[0])
        {
            string path = (t.Reg.GetString(PL + "\\" + key, "ProfileImagePath") ?? "").TrimEnd('\\');
            if (path != "") referenced.Add(path);
            bool bak = key.EndsWith(".bak", StringComparison.OrdinalIgnoreCase);
            string sidText = bak ? key.Substring(0, key.Length - 4) : key;
            if (!IsUserSid(sidText)) continue;
            var sid = new SecurityIdentifier(sidText);
            var p = new ProfileInfo { Sid = sidText, RegKey = key, Path = path };

            bool resolved = true;
            string name;
            if (local.TryGetValue(sidText, out name)) p.Account = t.Host + "\\" + name;
            else if (machineSid != null && machineSid.Equals(sid.AccountDomainSid)) resolved = false;
            else try { p.Account = sid.Translate(typeof(NTAccount)).Value; } catch (Exception) { resolved = false; }
            if (!resolved) p.Account = L.T("(nieznane konto)", "(unknown account)");

            p.Kind = Classify(sid, machineSid, resolved, dc);
            if (!bak) p.Role = RoleOf(t, sidText, machineSid != null && machineSid.Equals(sid.AccountDomainSid), resolved, dc, admins);
            p.LastLogon = LastLogon(t, PL + "\\" + key);
            p.Loaded = !bak && loaded.Contains(sidText);
            p.IsOperator = !bak && sidText == operatorSid;
            if (bak) p.Leftover = Leftover.BakKey;
            else if (fsOk && (path == "" || !Directory.Exists(t.Fs(path)))) p.Leftover = Leftover.NoFolder;
            snap.Profiles.Add(p);
        }

        if (fsOk)
            foreach (var d in new DirectoryInfo(t.Fs(profilesDir)).GetDirectories())
            {
                if ((d.Attributes & FileAttributes.ReparsePoint) != 0 || SystemFolders.Contains(d.Name)) continue;
                string real = profilesDir + "\\" + d.Name;
                if (!referenced.Contains(real))
                    snap.Profiles.Add(new ProfileInfo { Path = real, Account = L.T("(brak wpisu w rejestrze)", "(no registry entry)"), Leftover = Leftover.NoRegistry, Kind = Kind.UnknownNoDc });
            }

        foreach (var p in snap.Profiles) p.Excluded = IsExcluded(p, exclusions);
        snap.Profiles = snap.Profiles.OrderBy(x => x.Path, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.RegKey).ToList();

        if (profilesDir.Length >= 2 && char.IsLetter(profilesDir[0]) && profilesDir[1] == ':')
        {
            snap.Drive = profilesDir.Substring(0, 2);
            foreach (var o in t.Query("SELECT FreeSpace, Size FROM Win32_LogicalDisk WHERE DeviceID='" + snap.Drive + "'"))
            {
                snap.FreeBytes = Convert.ToInt64(o["FreeSpace"]);
                snap.TotalBytes = Convert.ToInt64(o["Size"]);
            }
        }
        return snap;
    }

    // -1 = folder nie istnieje lub jest niedostepny
    public static long DirSize(string path)
    {
        if (!Directory.Exists(path)) return -1;
        long total = 0;
        var stack = new Stack<string>();
        stack.Push(path);
        while (stack.Count > 0)
        {
            try
            {
                foreach (var f in new DirectoryInfo(stack.Pop()).EnumerateFileSystemInfos())
                {
                    if ((f.Attributes & FileAttributes.ReparsePoint) != 0) continue; // junctions w profilu zapetlaja
                    var fi = f as FileInfo;
                    if (fi != null) total += fi.Length; else stack.Push(f.FullName);
                }
            }
            catch (Exception) { } // brak dostepu / plik zniknal: rozmiar jest przyblizony
        }
        return total;
    }

    static string LongPath(string p)
    {
        if (p.StartsWith(@"\\?\")) return p;
        return p.StartsWith(@"\\") ? @"\\?\UNC\" + p.Substring(2) : @"\\?\" + p;
    }

    static void DeleteTree(string dir)
    {
        foreach (var e in new DirectoryInfo(dir).GetFileSystemInfos())
        {
            bool link = (e.Attributes & FileAttributes.ReparsePoint) != 0;
            if (e is DirectoryInfo && !link) { DeleteTree(e.FullName); continue; }
            if (!link) e.Attributes = FileAttributes.Normal; // atrybut tylko-do-odczytu blokuje Delete
            e.Delete(); // dla dowiazania usuwa samo dowiazanie, nigdy jego cel
        }
        var d = new DirectoryInfo(dir);
        d.Attributes = FileAttributes.Directory;
        d.Delete();
    }

    // Kopia ZIP folderu profilu. Zwraca null przy powodzeniu; kazdy pominiety plik to blad (kopia ma byc kompletna).
    static string Backup(Target t, ProfileInfo p, string dir)
    {
        string src = t.Fs(p.Path);
        if (!Directory.Exists(src)) return L.T("folder profilu jest niedostępny", "the profile folder is not accessible");
        Directory.CreateDirectory(dir);
        long need = p.SizeBytes >= 0 ? p.SizeBytes : DirSize(src);
        try
        {
            long free = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(dir))).AvailableFreeSpace;
            if (free < need)
                return L.T("za mało miejsca na kopię (potrzeba " + need / 1048576 + " MB, wolne " + free / 1048576 + " MB)",
                    "not enough space for the backup (" + need / 1048576 + " MB needed, " + free / 1048576 + " MB free)");
        }
        catch (ArgumentException) { } // cel na udziale sieciowym: wolnego miejsca nie sprawdzimy

        string zip = Path.Combine(dir, t.Host + "_" + p.Leaf + "_" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".zip");
        int skipped = 0;
        try
        {
            string root = LongPath(src);
            using (var fs = new FileStream(zip, FileMode.CreateNew))
            using (var za = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                var stack = new Stack<string>();
                stack.Push(root);
                while (stack.Count > 0)
                {
                    FileSystemInfo[] items;
                    try { items = new DirectoryInfo(stack.Pop()).GetFileSystemInfos(); }
                    catch (Exception) { skipped++; continue; }
                    foreach (var f in items)
                    {
                        if ((f.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                        if (f is DirectoryInfo) { stack.Push(f.FullName); continue; }
                        try { za.CreateEntryFromFile(f.FullName, f.FullName.Substring(root.Length + 1).Replace('\\', '/')); }
                        catch (Exception) { skipped++; }
                    }
                }
            }
        }
        catch (Exception e)
        {
            try { File.Delete(zip); } catch (Exception) { }
            return e.Message;
        }
        if (skipped == 0) return null;
        try { File.Delete(zip); } catch (Exception) { }
        return L.T("nie udało się skopiować " + skipped + " plików lub folderów", skipped + " files or folders could not be copied");
    }

    static bool IsLoaded(Target t, string sid)
    {
        return t.Query("SELECT Loaded FROM Win32_UserProfile WHERE SID='" + sid + "'").Any(o => (bool)o["Loaded"]);
    }

    // Zwraca null przy powodzeniu, inaczej opis bledu. Stan sprawdzamy na swiezo - mogl sie zmienic od wczytania listy.
    public static string Delete(Target t, ProfileInfo p, bool allowLogoff, string backupDir)
    {
        if (p.Excluded) return L.T("konto jest na liście wykluczeń", "the account is on the exclusion list");
        if (p.IsOperator) return L.T("to profil bieżącej sesji", "this is the profile of the current session");
        string inUse = L.T("profil jest w użyciu", "the profile is in use");
        string keyLeft = L.T("nie udało się usunąć klucza rejestru", "the registry key could not be deleted");
        string folderLeft = L.T("folder pozostał na dysku", "the folder is still on the disk");
        try
        {
            string key = PL + "\\" + p.RegKey;
            if (p.RegKey != "" && !IsUserSid(p.Sid)) return L.T("nieprawidłowy SID", "invalid SID"); // SID trafia do zapytan WQL
            string err;
            switch (p.Leftover)
            {
                case Leftover.None:
                    var o = t.Query("SELECT * FROM Win32_UserProfile WHERE SID='" + p.Sid + "'").FirstOrDefault();
                    if (o == null) return L.T("profil już nie istnieje", "the profile no longer exists");
                    if ((bool)o["Loaded"])
                    {
                        if (!allowLogoff) return inUse;
                        if (Wts.Logoff(t, p.Account) == 0)
                            return L.T("profil jest załadowany, ale konto nie ma sesji do wylogowania (usługa lub zadanie w tle?)",
                                "the profile is loaded but the account has no session to log off (a service or background task?)");
                        for (int i = 0; i < 30 && IsLoaded(t, p.Sid); i++) Thread.Sleep(2000);
                        if (IsLoaded(t, p.Sid)) return L.T("profil nadal załadowany po wylogowaniu", "the profile is still loaded after logoff");
                    }
                    if (backupDir != null && (err = Backup(t, p, backupDir)) != null)
                        return L.T("kopia nieudana, profil NIE został usunięty: ", "backup failed, the profile was NOT deleted: ") + err;
                    o.Delete(); // tak samo jak Windows: folder + rejestr
                    if (t.Reg.SubKeys(Reg.HKLM, key) != null) return L.T("wpis w rejestrze pozostał", "the registry entry is still there");
                    return Directory.Exists(t.Fs(p.Path)) ? folderLeft : null;

                case Leftover.BakKey:
                    if (!p.RegKey.EndsWith(".bak", StringComparison.OrdinalIgnoreCase) || t.Reg.SubKeys(Reg.HKLM, key) == null)
                        return L.T("klucz już nie istnieje", "the key no longer exists");
                    return t.Reg.DeleteTree(key) ? null : keyLeft;

                case Leftover.NoFolder:
                    if (t.Reg.SubKeys(Reg.HKLM, key) == null) return L.T("wpis już nie istnieje", "the entry no longer exists");
                    if (t.Reg.SubKeys(Reg.HKU, p.Sid) != null) return inUse;
                    string dir = (t.Reg.GetString(PL, "ProfilesDirectory") ?? "").TrimEnd('\\');
                    if (dir == "" || !Directory.Exists(t.Fs(dir)))
                        return L.T("brak dostępu do dysku - nie można potwierdzić braku folderu", "no disk access - cannot confirm the folder is missing");
                    if (p.Path != "" && Directory.Exists(t.Fs(p.Path))) return L.T("folder profilu jednak istnieje", "the profile folder exists after all");
                    if (!t.Reg.DeleteTree(key)) return keyLeft;
                    foreach (string g in t.Reg.SubKeys(Reg.HKLM, PG) ?? new string[0])
                        if (string.Equals(t.Reg.GetString(PG + "\\" + g, "SidString"), p.Sid, StringComparison.OrdinalIgnoreCase)) t.Reg.DeleteTree(PG + "\\" + g);
                    return null;

                default: // Leftover.NoRegistry
                    string profilesDir = (t.Reg.GetString(PL, "ProfilesDirectory") ?? "").TrimEnd('\\');
                    var di = new DirectoryInfo(t.Fs(p.Path));
                    if (!di.Exists) return L.T("folder już nie istnieje", "the folder no longer exists");
                    bool child = profilesDir != "" && string.Equals(Path.GetDirectoryName(p.Path.TrimEnd('\\')), profilesDir, StringComparison.OrdinalIgnoreCase);
                    if (!child || SystemFolders.Contains(di.Name) || (di.Attributes & FileAttributes.ReparsePoint) != 0)
                        return L.T("folder nie kwalifikuje się do usunięcia", "the folder does not qualify for deletion");
                    foreach (string k in t.Reg.SubKeys(Reg.HKLM, PL) ?? new string[0])
                        if (string.Equals((t.Reg.GetString(PL + "\\" + k, "ProfileImagePath") ?? "").TrimEnd('\\'), p.Path, StringComparison.OrdinalIgnoreCase))
                            return L.T("folder ma już wpis w rejestrze", "the folder now has a registry entry");
                    if (backupDir != null && (err = Backup(t, p, backupDir)) != null)
                        return L.T("kopia nieudana, folder NIE został usunięty: ", "backup failed, the folder was NOT deleted: ") + err;
                    DeleteTree(LongPath(di.FullName));
                    return Directory.Exists(t.Fs(p.Path)) ? folderLeft : null;
            }
        }
        catch (Exception e) { return e.Message; }
    }

    public static void Log(string msg, EventLogEntryType type)
    {
        try
        {
            if (!EventLog.SourceExists(EventSource)) EventLog.CreateEventSource(EventSource, "Application");
            EventLog.WriteEntry(EventSource, msg.Length > 30000 ? msg.Substring(0, 30000) : msg, type);
        }
        catch (Exception) { } // brak dziennika nie moze blokowac pracy
    }

    // Usuwa wskazane profile, zwraca raport; failed = liczba bledow. Wpis trafia do dziennika na komputerze operatora.
    public static string DeleteMany(Target t, IEnumerable<ProfileInfo> sel, bool allowLogoff, string backupDir, bool cli, Action<ProfileInfo> progress, out int failed)
    {
        failed = 0;
        var report = new StringBuilder();
        foreach (var p in sel)
        {
            if (progress != null) progress(p);
            string err = Delete(t, p, allowLogoff, backupDir);
            if (err != null) failed++;
            report.AppendLine((err == null ? L.T("USUNIĘTO: ", "DELETED: ") : L.T("BŁĄD (", "ERROR (") + err + "): ") + p.Describe);
        }
        string header = L.T("Komputer: ", "Computer: ") + t.Host + "\r\n" +
            L.T("Uruchomił", "Run by") + (cli ? L.T(" (wiersz poleceń)", " (command line)") : "") + ": " + WindowsIdentity.GetCurrent().Name +
            (backupDir != null ? "\r\n" + L.T("Kopie ZIP: ", "ZIP backups: ") + backupDir : "") + "\r\n" + L.T("Błędów: ", "Errors: ") + failed + "\r\n\r\n";
        Log(header + report, failed > 0 ? EventLogEntryType.Warning : EventLogEntryType.Information);
        return header + report;
    }

    public static string CsvField(string s)
    {
        return s.IndexOfAny(new[] { ';', '"', '\r', '\n' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
    }
    static string Mb(long bytes) { return bytes < 0 ? "" : (bytes / 1048576).ToString(); }

    public static string Csv(IEnumerable<ProfileInfo> list, string host)
    {
        var sb = new StringBuilder(L.T("Komputer;Konto;Typ;Rola;Ostatnie logowanie;Wiek (dni);Rozmiar (MB);Pulpit (MB);Dokumenty (MB);Pobrane (MB);Stan;Folder;Klucz rejestru",
            "Computer;Account;Type;Role;Last logon;Age (days);Size (MB);Desktop (MB);Documents (MB);Downloads (MB);State;Folder;Registry key") + "\r\n");
        foreach (var p in list)
            sb.AppendLine(string.Join(";", new[] {
                host, p.Account, p.KindText, p.RoleText, p.LastLogon.HasValue ? p.LastLogon.Value.ToString("yyyy-MM-dd HH:mm") : "",
                p.AgeDays.HasValue ? p.AgeDays.Value.ToString() : "", Mb(p.SizeBytes), Mb(p.Desktop), Mb(p.Documents), Mb(p.Downloads),
                p.StateText, p.Path, p.RegKey }.Select(CsvField)));
        return sb.ToString();
    }

    static void Check(bool ok, string what) { if (!ok) throw new Exception(what); }

    public static int SelfTest(TextWriter o)
    {
        bool lang = L.En;
        try
        {
            var m = new SecurityIdentifier("S-1-5-21-1-2-3");
            var loc = new SecurityIdentifier("S-1-5-21-1-2-3-1001");
            var dom = new SecurityIdentifier("S-1-5-21-9-9-9-1105");
            Check(Classify(loc, m, true, false) == Kind.Local, "local");
            Check(Classify(loc, m, false, true) == Kind.OrphanLocal, "orphan local");
            Check(Classify(dom, m, true, false) == Kind.Domain, "domain");
            Check(Classify(dom, m, false, true) == Kind.OrphanDomain, "orphan domain");
            Check(Classify(dom, m, false, false) == Kind.UnknownNoDc, "unknown without DC");
            Check(Classify(dom, null, true, true) == Kind.Domain, "no machine sid");

            var t = new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            long ft = t.ToFileTimeUtc();
            Check(FileTimeFromParts((uint)(ft >> 32), unchecked((uint)ft)) == t.ToLocalTime(), "filetime");
            Check(FileTimeFromParts(null, 5) == null, "filetime null");
            Check(FileTimeFromParts(0, 0) == null, "filetime zero");

            var p = new ProfileInfo { Sid = "S-1-5-21-9-9-9-1105", RegKey = "S-1-5-21-9-9-9-1105", Path = @"C:\Users\jan", Account = @"DOM\jan", Kind = Kind.Domain, LastLogon = DateTime.Now.AddDays(-100) };
            Check(ShouldSelect(p, 90, true, true), "old domain selected");
            Check(!ShouldSelect(p, 101, true, true), "young not selected");
            p.Role = Role.Admin; Check(!ShouldSelect(p, 90, false, true) && ShouldSelect(p, 90, false, false) && !SelectAuto(p, 0, false, true, false, false), "admins skipped unless asked");
            p.Role = Role.BuiltinAdmin; Check(!ShouldSelect(p, 90, false, true), "built-in admin skipped"); p.Role = Role.User;
            Check(DecideRole(Role.Unknown, true, true, false, false, false, null) == Role.User && DecideRole(Role.Unknown, true, true, false, false, true, null) == Role.Admin, "role from cached groups when the domain cannot be asked");
            Check(DecideRole(Role.Unknown, true, true, false, false, false, true) == Role.Admin && DecideRole(Role.Unknown, true, true, false, false, null, null) == Role.Unknown && DecideRole(Role.Unknown, false, true, true, false, true, true) == Role.Unknown, "role: fresh domain answer wins, no facts means unknown");
            Check(WellKnownRole("S-1-5-21-1-2-3-500") == Role.BuiltinAdmin && WellKnownRole("S-1-5-21-1-2-3-501") == Role.Guest && WellKnownRole("S-1-5-21-1-2-3-1500") == Role.Unknown && WellKnownRole("S-1-12-1-1-2-3-500") == Role.Unknown, "well-known roles");
            p.Loaded = true; Check(!ShouldSelect(p, 0, false, false), "loaded never selected"); Check(p.CanDelete(true) && !p.CanDelete(false), "loaded needs logoff"); p.Loaded = false;
            p.Kind = Kind.Local; Check(!ShouldSelect(p, 90, true, false), "local skipped"); Check(ShouldSelect(p, 90, false, false), "local included");
            p.Kind = Kind.OrphanLocal; Check(!ShouldSelect(p, 90, true, false), "orphan local skipped"); Check(SelectAuto(p, 99999, true, true, true, false), "auto orphans");
            p.Kind = Kind.UnknownNoDc; Check(!ShouldSelect(p, 0, false, false), "unverified never selected"); Check(!SelectAuto(p, 0, false, false, true, true), "auto skips unverified");
            p.Kind = Kind.Domain; p.LastLogon = null; Check(!ShouldSelect(p, 0, false, false), "unknown age never selected");
            p.LastLogon = DateTime.Now.AddDays(-100);
            p.Leftover = Leftover.BakKey; Check(!ShouldSelect(p, 0, false, false), "leftover not selected by age"); Check(SelectAuto(p, 99999, true, true, false, true) && !SelectAuto(p, 0, false, false, true, false), "auto leftovers"); p.Leftover = Leftover.None;
            p.IsOperator = true; Check(!p.CanDelete(true) && !SelectAuto(p, 0, false, false, true, true), "operator protected"); p.IsOperator = false;

            Func<string, HashSet<string>> ex = s => new HashSet<string>(new[] { s }, StringComparer.OrdinalIgnoreCase);
            Check(IsExcluded(p, ex("jan")) && IsExcluded(p, ex(@"dom\JAN")) && IsExcluded(p, ex("S-1-5-21-9-9-9-1105")), "exclusion by name/account/sid");
            Check(!IsExcluded(p, ex("ja")) && !IsExcluded(p, ex("")), "exclusion needs exact match");
            p.Excluded = true; Check(!p.CanDelete(true) && !ShouldSelect(p, 0, false, false) && !SelectAuto(p, 0, false, false, true, true), "excluded protected"); p.Excluded = false;
            var folderOnly = new ProfileInfo { Path = @"C:\Users\stary.DOM", Account = "-", Leftover = Leftover.NoRegistry };
            Check(IsExcluded(folderOnly, ex("stary.dom")) && folderOnly.Id == folderOnly.Path, "folder leftover identity");

            Check(IsUserSid("S-1-5-21-1-2-3-500") && IsUserSid("S-1-12-1-1-2-3-4"), "user sids");
            Check(!IsUserSid("S-1-5-18") && !IsUserSid("S-1-5-82-1-2-3-4-5") && !IsUserSid("S-1-5-21-x") && !IsUserSid("x' OR '1'='1"), "non-user sids");
            Check(Target.FsPath("PC7", false, @"C:\Users\a") == @"\\PC7\C$\Users\a" && Target.FsPath("PC7", true, @"C:\Users\a") == @"C:\Users\a", "remote path");
            Check(CsvField("a;b") == "\"a;b\"" && CsvField("x\"y") == "\"x\"\"y\"" && CsvField("abc") == "abc", "csv quoting");
            L.En = false;
            Check(Csv(new[] { p }, "PC7").Split('\n')[1].StartsWith(@"PC7;DOM\jan;Domenowe;Użytkownik;") && folderOnly.KindText == "Pozostałość: folder bez wpisu", "polish texts");
            L.En = true;
            Check(Csv(new[] { p }, "PC7").Split('\n')[1].StartsWith(@"PC7;DOM\jan;Domain;User;") && folderOnly.KindText == "Leftover: folder without entry", "english texts");
            o.WriteLine("selftest OK");
            return 0;
        }
        catch (Exception e)
        {
            o.WriteLine("selftest FAILED: " + e.Message);
            return 1;
        }
        finally { L.En = lang; }
    }
}

// Tryb bez okna: 0 = OK, 1 = byly bledy usuwania, 2 = zle parametry, 3 = blad krytyczny.
static class Cli
{
    static readonly string[] Flags = { "--help", "--selftest", "--dump", "--delete", "--auto", "--whatif", "--include-local", "--include-admins", "--orphans", "--leftovers" };
    static readonly string[] Valued = { "--computer", "--backup", "--days", "--lang" };

    static string Usage
    {
        get
        {
            return L.T(
                "ProfileJanitor.exe                      okno programu\r\n" +
                "  --dump [--computer NAZWA]             lista profili\r\n" +
                "  --delete ID... [--backup DIR] [--computer NAZWA] [--whatif]\r\n" +
                "                                        usuwa wskazane pozycje; ID = SID, klucz SID.bak albo ścieżka folderu\r\n" +
                "  --auto --days N [--include-local] [--include-admins] [--orphans] [--leftovers] [--backup DIR] [--computer NAZWA] [--whatif]\r\n" +
                "                                        usuwa profile nieużywane od N dni (domyślnie pomija konta lokalne i administratorów)\r\n" +
                "  --whatif                              tylko pokazuje, co zostałoby usunięte\r\n" +
                "  --lang pl|en                          język komunikatów\r\n" +
                "  --help                                ten opis\r\n" +
                "Kod wyjścia: 0 = w porządku, 1 = były błędy usuwania, 2 = złe parametry, 3 = błąd krytyczny.\r\n" +
                "Tryb --auto nie pyta o potwierdzenie - zacznij od --whatif. Szczegóły: klawisz F1 w oknie programu.\r\n" +
                "Wykluczenia na stałe: plik <nazwa programu>.exclude.txt obok programu.",
                "ProfileJanitor.exe                      program window\r\n" +
                "  --dump [--computer NAME]              list profiles\r\n" +
                "  --delete ID... [--backup DIR] [--computer NAME] [--whatif]\r\n" +
                "                                        delete the given items; ID = SID, SID.bak key or folder path\r\n" +
                "  --auto --days N [--include-local] [--include-admins] [--orphans] [--leftovers] [--backup DIR] [--computer NAME] [--whatif]\r\n" +
                "                                        delete profiles unused for N days (local accounts and administrators skipped by default)\r\n" +
                "  --whatif                              only show what would be deleted\r\n" +
                "  --lang pl|en                          message language\r\n" +
                "  --help                                this text\r\n" +
                "Exit code: 0 = fine, 1 = some deletions failed, 2 = bad parameters, 3 = fatal error.\r\n" +
                "--auto does not ask for confirmation - start with --whatif. Details: press F1 in the program window.\r\n" +
                "Permanent exclusions: file <program name>.exclude.txt next to the program.");
        }
    }

    [DllImport("kernel32.dll")] static extern bool AttachConsole(int processId);
    [DllImport("kernel32.dll")] static extern IntPtr GetStdHandle(int which);

    public static int Run(string[] args)
    {
        // Program okienkowy nie ma wlasnej konsoli. Uruchomiony z wiersza polecen bez przekierowania wyniku
        // podpina sie pod konsole, z ktorej go wywolano - inaczej --help i raporty nie bylyby nigdzie widoczne.
        IntPtr stdout = GetStdHandle(-11);
        if (stdout == IntPtr.Zero || stdout == new IntPtr(-1))
        {
            AttachConsole(-1);
            try { Console.OutputEncoding = Encoding.UTF8; } catch (Exception) { }
        }
        using (var o = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)))
        {
            try { return Run(args, o); }
            catch (Exception e) { o.WriteLine(L.T("BŁĄD: ", "ERROR: ") + e.Message); return 3; }
        }
    }

    // Wejscie programu: null = otworz okno, liczba = kod wyjscia trybu wiersza polecen.
    public static int? Start(string[] args)
    {
        int li = Array.FindIndex(args, a => string.Equals(a, "--lang", StringComparison.OrdinalIgnoreCase));
        L.Init(li >= 0 && li + 1 < args.Length ? args[li + 1] : null);
        bool onlyLang = args.Length == 2 && li == 0; // "--lang en" bez innych parametrow otwiera okno w tym jezyku
        if (args.Length > 0 && !onlyLang) return Run(args);
        if (new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator)) return null;
        return -1;
    }
    public static string NeedAdmin { get { return L.T("Program wymaga uprawnień administratora.", "This program requires administrator rights."); } }

    static int Run(string[] args, TextWriter o)
    {
        var opt = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var ids = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            if (!a.StartsWith("--")) { ids.Add(a); continue; }
            if (Valued.Contains(a, StringComparer.OrdinalIgnoreCase) && i + 1 < args.Length) opt[a] = args[++i];
            else if (Flags.Contains(a, StringComparer.OrdinalIgnoreCase)) opt[a] = "";
            else { o.WriteLine(L.T("Nieznany lub niepełny parametr: ", "Unknown or incomplete parameter: ") + a + "\r\n\r\n" + Usage); return 2; } // literowka nie moze byc po cichu pominieta
        }
        if (opt.ContainsKey("--help")) { o.WriteLine(Usage); return 0; }
        if (opt.ContainsKey("--selftest")) return Core.SelfTest(o);

        int modes = new[] { "--dump", "--delete", "--auto" }.Count(opt.ContainsKey);
        int days = -1;
        bool bad = modes != 1 || (ids.Count > 0) != opt.ContainsKey("--delete");
        if (opt.ContainsKey("--auto") && (!opt.ContainsKey("--days") || !int.TryParse(opt["--days"], out days) || days < 0)) bad = true;
        if (opt.ContainsKey("--lang") && !new[] { "pl", "en" }.Contains(opt["--lang"], StringComparer.OrdinalIgnoreCase)) bad = true;
        if (bad) { o.WriteLine(Usage); return 2; }

        var t = new Target(opt.ContainsKey("--computer") ? opt["--computer"] : "");
        var snap = Core.Load(t, Core.LoadExclusions());

        if (opt.ContainsKey("--dump"))
        {
            o.WriteLine(snap.Status);
            foreach (var p in snap.Profiles)
                o.WriteLine(string.Join("\t", p.Account, p.KindText, p.RoleText == "" ? "-" : p.RoleText,
                    p.LastLogon.HasValue ? p.LastLogon.Value.ToString("yyyy-MM-dd HH:mm") : L.T("brak danych", "no data"),
                    p.AgeDays.HasValue ? p.AgeDays.Value.ToString() : "?",
                    p.Path == "" ? "-" : (Core.DirSize(t.Fs(p.Path)) / 1048576) + " MB", p.StateText == "" ? "-" : p.StateText, p.Path, p.RegKey));
            return 0;
        }

        var sel = new List<ProfileInfo>();
        int missing = 0;
        if (opt.ContainsKey("--delete"))
            foreach (string id in ids)
            {
                var p = snap.Profiles.FirstOrDefault(x => string.Equals(x.Id, id.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));
                if (p != null) sel.Add(p);
                else { missing++; o.WriteLine(L.T("BŁĄD (nie ma takiej pozycji): ", "ERROR (no such item): ") + id); }
            }
        else
            sel = snap.Profiles.Where(p => Core.SelectAuto(p, days, !opt.ContainsKey("--include-local"), !opt.ContainsKey("--include-admins"), opt.ContainsKey("--orphans"), opt.ContainsKey("--leftovers"))).ToList();

        if (opt.ContainsKey("--whatif"))
        {
            foreach (var p in sel) o.WriteLine(L.T("DO USUNIĘCIA: ", "WOULD DELETE: ") + p.Describe);
            o.WriteLine(L.T("Razem: " + sel.Count + " (nic nie usunięto)", "Total: " + sel.Count + " (nothing deleted)"));
            return missing > 0 ? 1 : 0;
        }
        if (sel.Count == 0) { o.WriteLine(L.T("Nic do usunięcia.", "Nothing to delete.")); return missing > 0 ? 1 : 0; }
        int failed;
        o.Write(Core.DeleteMany(t, sel, false, opt.ContainsKey("--backup") ? opt["--backup"] : null, true, null, out failed));
        return failed + missing > 0 ? 1 : 0;
    }
}
