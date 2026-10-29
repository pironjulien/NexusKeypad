// Mouchard écran / pad : note dans %LOCALAPPDATA%\NexusKeypad\mouchard.log tout ce qui peut expliquer un pad resté allumé
// écran éteint : état de l'affichage annoncé par Windows, verrouillage, démarrages de Logi, appuis sur le pad (rapports
// HID du MX Creative Keypad, VID 046D PID C354), alimentation de l'écran lue par DDC/CI, lignes du journal du plugin.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

public sealed class Mouchard : NativeWindow, IDisposable
{
    const int WM_POWERBROADCAST = 0x218, PBT_POWERSETTINGCHANGE = 0x8013, WM_WTSSESSION_CHANGE = 0x2B1, WM_INPUT = 0xFF;
    static readonly Dictionary<Guid, string> Reglages = new Dictionary<Guid, string>
    {
        { new Guid("6FE69556-704A-47A0-8F24-C28D936FDA47"), "affichage console" },
        { new Guid("2B84C20E-AD23-4DDF-93DB-05FFBD7EFCA5"), "affichage session" },
        { new Guid("02731015-4510-4526-99E6-E5A17EBD1AEA"), "moniteur" },
        { new Guid("3C0F4548-C03F-4C4D-B9F2-237EDE686376"), "présence" },
    };
    static readonly string[] Etats = { "éteint", "allumé", "atténué" };
    static readonly string[] Sessions = { "", "console connectée", "console déconnectée", "distante connectée", "distante déconnectée", "ouverture", "fermeture", "verrouillage", "déverrouillage", "contrôle à distance" };

    readonly string _journal;
    readonly string _journalPlugin;
    readonly HashSet<IntPtr> _pad = new HashSet<IntPtr>();
    readonly System.Windows.Forms.Timer _tour = new System.Windows.Forms.Timer { Interval = 2000 };
    Dictionary<string, int> _logi = new Dictionary<string, int>();
    long _positionPlugin = -1;
    DateTime _dernierAppui = DateTime.MinValue;
    string _ddc = null;
    int _tic;

    public Mouchard(string journal, string journalPlugin)
    {
        _journal = journal;
        _journalPlugin = journalPlugin;
        CreateHandle(new CreateParams { Caption = "NexusKeypad mouchard" }); // fenêtre cachée de premier niveau
        foreach (var g in Reglages.Keys)
        {
            var copie = g;
            RegisterPowerSettingNotification(Handle, ref copie, 0);
        }
        WTSRegisterSessionNotification(Handle, 0);
        Ecouter();
        _logi = Logi();
        Noter("démarrage ; Logi : " + Decrire(_logi) + " ; inactivité " + Inactif() + " s");
        _tour.Tick += (s, e) => Tour();
        _tour.Start();
    }

    // Appuis sur le pad : chaque collection HID du keypad, reçue même fenêtre cachée (RIDEV_INPUTSINK).
    void Ecouter()
    {
        uint n = 0;
        var taille = (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICELIST));
        GetRawInputDeviceList(null, ref n, taille);
        var liste = new RAWINPUTDEVICELIST[n];
        GetRawInputDeviceList(liste, ref n, taille);
        var pages = new List<RAWINPUTDEVICE>();
        foreach (var d in liste.Where(d => d.dwType == 2))
        {
            uint lon = 0;
            GetRawInputDeviceInfo(d.hDevice, 0x20000007, null, ref lon);
            var nom = new StringBuilder((int)lon + 1);
            GetRawInputDeviceInfo(d.hDevice, 0x20000007, nom, ref lon);
            if (nom.ToString().IndexOf("VID_046D&PID_C354", StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            var info = new RID_DEVICE_INFO { cbSize = (uint)Marshal.SizeOf(typeof(RID_DEVICE_INFO)) };
            var t = info.cbSize;
            GetRawInputDeviceInfo(d.hDevice, 0x2000000b, ref info, ref t);
            _pad.Add(d.hDevice);
            if (!pages.Any(p => p.usUsagePage == info.hid.usUsagePage && p.usUsage == info.hid.usUsage))
            {
                pages.Add(new RAWINPUTDEVICE { usUsagePage = info.hid.usUsagePage, usUsage = info.hid.usUsage, dwFlags = 0x100, hwndTarget = Handle });
            }
        }

        var ok = pages.Count > 0 && RegisterRawInputDevices(pages.ToArray(), (uint)pages.Count, (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICE)));
        Noter("pad : " + _pad.Count + " collection(s) HID, écoute " + (ok ? "active" : "impossible") + " (" + String.Join(", ", pages.Select(p => p.usUsagePage.ToString("X4") + ":" + p.usUsage.ToString("X2"))) + ")");
    }

    void Tour()
    {
        _tic++;
        var logi = Logi();
        if (!logi.OrderBy(k => k.Key).SequenceEqual(_logi.OrderBy(k => k.Key)))
        {
            Noter("Logi : " + Decrire(_logi) + " -> " + Decrire(logi));
            _logi = logi;
        }

        if (_tic % 5 == 0)
        {
            var ddc = Ddc();
            if (ddc != _ddc)
            {
                Noter("écran (DDC/CI) : " + ddc);
                _ddc = ddc;
            }
        }

        Plugin();
    }

    // Nouvelles lignes du journal du plugin (recréé à chaque chargement : relu depuis le début s'il raccourcit).
    void Plugin()
    {
        try
        {
            using (var f = new FileStream(_journalPlugin, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                if (_positionPlugin < 0 || f.Length < _positionPlugin)
                {
                    _positionPlugin = _positionPlugin < 0 ? f.Length : 0;
                }

                f.Position = _positionPlugin;
                var texte = new StreamReader(f, Encoding.UTF8).ReadToEnd();
                _positionPlugin = f.Length;
                foreach (var l in texte.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && !l.Contains("conversations :")))
                {
                    Noter("plugin | " + l);
                }
            }
        }
        catch (IOException)
        {
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_POWERBROADCAST && (int)m.WParam == PBT_POWERSETTINGCHANGE)
        {
            var g = (Guid)Marshal.PtrToStructure(m.LParam, typeof(Guid));
            var v = Marshal.ReadInt32(m.LParam, 20);
            string nom;
            Reglages.TryGetValue(g, out nom);
            var etat = nom == "présence" ? (v == 0 ? "présent" : v == 2 ? "absent" : v.ToString()) : v >= 0 && v < Etats.Length ? Etats[v] : v.ToString();
            Noter((nom ?? g.ToString()) + " : " + etat + " (inactivité " + Inactif() + " s)");
        }
        else if (m.Msg == WM_WTSSESSION_CHANGE)
        {
            var c = (int)m.WParam;
            Noter("session : " + (c > 0 && c < Sessions.Length ? Sessions[c] : c.ToString()));
        }
        else if (m.Msg == WM_INPUT)
        {
            uint t = 0;
            var entete = (uint)Marshal.SizeOf(typeof(RAWINPUTHEADER));
            GetRawInputData(m.LParam, 0x10000003, IntPtr.Zero, ref t, entete);
            var tampon = Marshal.AllocHGlobal((int)t);
            try
            {
                if (GetRawInputData(m.LParam, 0x10000003, tampon, ref t, entete) == t)
                {
                    var h = (RAWINPUTHEADER)Marshal.PtrToStructure(tampon, typeof(RAWINPUTHEADER));
                    if (_pad.Contains(h.hDevice) && (DateTime.Now - _dernierAppui).TotalSeconds > 2)
                    {
                        Noter("pad : appui ou rapport HID");
                    }

                    if (_pad.Contains(h.hDevice))
                    {
                        _dernierAppui = DateTime.Now;
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(tampon);
            }
        }

        base.WndProc(ref m);
    }

    void Noter(string ligne)
    {
        try
        {
            File.AppendAllText(_journal, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + ligne + Environment.NewLine, Encoding.UTF8);
        }
        catch (IOException)
        {
        }
    }

    static Dictionary<string, int> Logi()
    {
        var r = new Dictionary<string, int>();
        foreach (var n in new[] { "logioptionsplus_agent", "LogiPluginService" })
        {
            var p = Process.GetProcessesByName(n).FirstOrDefault();
            r[n] = p == null ? 0 : p.Id;
        }

        return r;
    }

    static string Decrire(Dictionary<string, int> logi)
    {
        return String.Join(", ", logi.Select(k => k.Key + " " + (k.Value == 0 ? "absent" : "pid " + k.Value)));
    }

    static long Inactif()
    {
        var i = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO)) };
        return GetLastInputInfo(ref i) ? (Environment.TickCount - (int)i.dwTime) / 1000 : -1;
    }

    // Alimentation des écrans selon eux-mêmes (VCP D6 : 1 allumé, 2-5 veille ou éteint) ; muet = sans réponse DDC.
    static string Ddc()
    {
        var etats = new List<string>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hm, IntPtr hdc, IntPtr r, IntPtr d) =>
        {
            uint n;
            if (!GetNumberOfPhysicalMonitorsFromHMONITOR(hm, out n) || n == 0)
            {
                return true;
            }

            var phys = new PHYSICAL_MONITOR[n];
            if (GetPhysicalMonitorsFromHMONITOR(hm, n, phys))
            {
                foreach (var p in phys)
                {
                    uint type, actuel, max;
                    etats.Add(GetVCPFeatureAndVCPFeatureReply(p.hPhysicalMonitor, 0xD6, out type, out actuel, out max) ? (actuel == 1 ? "allumé" : "D6=" + actuel) : "muet");
                }

                DestroyPhysicalMonitors(n, phys);
            }

            return true;
        }, IntPtr.Zero);
        return etats.Count == 0 ? "aucun" : String.Join(", ", etats);
    }

    public void Dispose()
    {
        DestroyHandle();
    }

    public static void Main(string[] args)
    {
        bool seul;
        using (new Mutex(true, "Local\\NexusKeypadMouchard", out seul))
        {
            if (!seul)
            {
                return;
            }

            var dossier = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NexusKeypad");
            Directory.CreateDirectory(dossier);
            var plugin = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Logi", "LogiPluginService", "Logs", "plugin_logs", "NexusKeypad.log");
            using (new Mouchard(Path.Combine(dossier, "mouchard.log"), plugin))
            {
                Application.Run();
            }
        }
    }

    delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, IntPtr rect, IntPtr data);
    [StructLayout(LayoutKind.Sequential)] struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }
    [StructLayout(LayoutKind.Sequential)] struct RAWINPUTDEVICELIST { public IntPtr hDevice; public uint dwType; }
    [StructLayout(LayoutKind.Sequential)] struct RAWINPUTDEVICE { public ushort usUsagePage; public ushort usUsage; public uint dwFlags; public IntPtr hwndTarget; }
    [StructLayout(LayoutKind.Sequential)] struct RAWINPUTHEADER { public uint dwType; public uint dwSize; public IntPtr hDevice; public IntPtr wParam; }
    [StructLayout(LayoutKind.Sequential)] struct RID_DEVICE_INFO_HID { public uint dwVendorId; public uint dwProductId; public uint dwVersionNumber; public ushort usUsagePage; public ushort usUsage; }
    [StructLayout(LayoutKind.Explicit)] struct RID_DEVICE_INFO { [FieldOffset(0)] public uint cbSize; [FieldOffset(4)] public uint dwType; [FieldOffset(8)] public RID_DEVICE_INFO_HID hid; [FieldOffset(8)] ulong pad0; [FieldOffset(16)] ulong pad1; [FieldOffset(24)] ulong pad2; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct PHYSICAL_MONITOR { public IntPtr hPhysicalMonitor; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szPhysicalMonitorDescription; }

    [DllImport("user32.dll")] static extern IntPtr RegisterPowerSettingNotification(IntPtr h, ref Guid g, int flags);
    [DllImport("wtsapi32.dll")] static extern bool WTSRegisterSessionNotification(IntPtr h, int flags);
    [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO i);
    [DllImport("user32.dll")] static extern uint GetRawInputDeviceList([Out] RAWINPUTDEVICELIST[] l, ref uint n, uint taille);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint GetRawInputDeviceInfo(IntPtr h, uint cmd, StringBuilder data, ref uint taille);
    [DllImport("user32.dll")] static extern uint GetRawInputDeviceInfo(IntPtr h, uint cmd, ref RID_DEVICE_INFO data, ref uint taille);
    [DllImport("user32.dll")] static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] d, uint n, uint taille);
    [DllImport("user32.dll")] static extern uint GetRawInputData(IntPtr h, uint cmd, IntPtr data, ref uint taille, uint entete);
    [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc proc, IntPtr data);
    [DllImport("dxva2.dll")] static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hm, out uint n);
    [DllImport("dxva2.dll")] static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hm, uint n, [Out] PHYSICAL_MONITOR[] p);
    [DllImport("dxva2.dll")] static extern bool DestroyPhysicalMonitors(uint n, PHYSICAL_MONITOR[] p);
    [DllImport("dxva2.dll")] static extern bool GetVCPFeatureAndVCPFeatureReply(IntPtr h, byte code, out uint type, out uint actuel, out uint max);
}
