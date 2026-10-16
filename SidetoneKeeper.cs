// Держит мониторинг микрофона (sidetone) HyperX Cloud Alpha S включённым.
// Раз в 10 с спрашивает состояние гарнитуры по HID; если мониторинг выключен (после перезагрузки,
// переподключения, перезапуска NGenuity), включает его. Протокол взят из NGenuity2Helper:
// CMDHSCloudAlphaSGetDeviceStatus (ответ: r[14] бит 0 = мониторинг) и CMDHSCloudAlphaSSetSidetone (переключатель).
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

static class SidetoneKeeper {
  const string DeviceTag = "vid_03f0&pid_0c91&mi_05&col03";
  const int Len = 62;
  static readonly string LogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sidetone.log");

  [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
  static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr sa, uint disp, uint flags, IntPtr tmpl);
  [DllImport("hid.dll", SetLastError = true)] static extern bool HidD_SetOutputReport(SafeFileHandle h, byte[] b, int n);
  [DllImport("hid.dll", SetLastError = true)] static extern bool HidD_GetInputReport(SafeFileHandle h, byte[] b, int n);
  [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid g);
  [DllImport("setupapi.dll", SetLastError = true)] static extern IntPtr SetupDiGetClassDevs(ref Guid g, IntPtr e, IntPtr w, int flags);
  [DllImport("setupapi.dll", SetLastError = true)] static extern bool SetupDiEnumDeviceInterfaces(IntPtr s, IntPtr d, ref Guid g, int i, ref SP_DEVICE_INTERFACE_DATA data);
  [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
  static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr s, ref SP_DEVICE_INTERFACE_DATA data, IntPtr detail, int size, out int req, IntPtr dev);
  [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr s);
  [StructLayout(LayoutKind.Sequential)] struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid g; public int flags; public IntPtr res; }

  static string FindDevice() {
    Guid g; HidD_GetHidGuid(out g);
    IntPtr set = SetupDiGetClassDevs(ref g, IntPtr.Zero, IntPtr.Zero, 0x12); // DIGCF_PRESENT | DIGCF_DEVICEINTERFACE
    try {
      for (int i = 0; ; i++) {
        var d = new SP_DEVICE_INTERFACE_DATA(); d.cbSize = Marshal.SizeOf(d);
        if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref g, i, ref d)) return null;
        int req; SetupDiGetDeviceInterfaceDetail(set, ref d, IntPtr.Zero, 0, out req, IntPtr.Zero);
        IntPtr buf = Marshal.AllocHGlobal(req);
        try {
          Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6);
          if (SetupDiGetDeviceInterfaceDetail(set, ref d, buf, req, out req, IntPtr.Zero)) {
            string path = Marshal.PtrToStringUni(buf + 4);
            if (path.ToLowerInvariant().Contains(DeviceTag)) return path;
          }
        } finally { Marshal.FreeHGlobal(buf); }
      }
    } finally { SetupDiDestroyDeviceInfoList(set); }
  }

  static byte[] Cmd(byte count, byte op, byte arg) {
    var b = new byte[Len]; b[0] = 6; b[2] = count; b[4] = op; b[7] = 0x68; b[8] = 0x4A; b[9] = 0x8E; b[10] = arg; return b;
  }
  static byte[] Exchange(SafeFileHandle h, byte[] cmd, int waitMs) {
    if (!HidD_SetOutputReport(h, cmd, cmd.Length)) {
      using (var fs = new FileStream(h, FileAccess.Write, Len, false)) fs.Write(cmd, 0, cmd.Length);
    }
    Thread.Sleep(waitMs);
    var r = new byte[Len]; r[0] = 6;
    if (!HidD_GetInputReport(h, r, r.Length)) throw new IOException("HidD_GetInputReport: " + Marshal.GetLastWin32Error());
    return r;
  }
  // null — ответ не распознан (например, его перехватил NGenuity); тогда просто пробуем позже.
  static bool? SidetoneOn(SafeFileHandle h) {
    var r = Exchange(h, Cmd(1, 0x9B, 9), 100);
    if (r[4] != 0x9B || r[7] != 0x68 || r[8] != 0x4A || r[9] != 0x8E) return null;
    return (r[14] & 1) != 0;
  }

  static void Log(string msg) {
    try {
      if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 256 * 1024) File.Delete(LogPath);
      File.AppendAllText(LogPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + msg + Environment.NewLine);
    } catch { }
  }

  static void Main() {
    bool created;
    using (new Mutex(true, @"Local\HyperXSidetoneKeeper", out created)) {
      if (!created) return;
      Log("started");
      string lastError = null;
      while (true) {
        try {
          string path = FindDevice();
          if (path != null) {
            using (var h = CreateFile(path, 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero)) {
              if (h.IsInvalid) throw new IOException("CreateFile: " + Marshal.GetLastWin32Error());
              if (SidetoneOn(h) == false) {
                Exchange(h, Cmd(1, 0x9B, 7), 50);
                Thread.Sleep(300);
                Log("мониторинг был выключен, включён; проверка: " + SidetoneOn(h));
              }
            }
          }
          lastError = null;
        } catch (Exception e) {
          if (e.Message != lastError) Log("ошибка: " + e.Message);
          lastError = e.Message;
        }
        Thread.Sleep(10000);
      }
    }
  }
}
