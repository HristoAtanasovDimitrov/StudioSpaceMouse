using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace StudioSpaceMouse
{
    /// <summary>
    /// Receives 3Dconnexion HID reports through the Windows Raw Input API.
    /// A hidden top-level window on a background thread registers for
    /// multi-axis controllers (usage page 1, usage 8) with RIDEV_INPUTSINK, so
    /// reports arrive whether or not Studio's own window has focus. The caller
    /// decides whether to act on them. Nothing here touches Unity.
    /// </summary>
    internal sealed class RawInputReader : IDisposable
    {
        private const int VendorLogitech = 0x046D;      // older SpaceNavigator / SpacePilot
        private const int Vendor3Dconnexion = 0x256F;   // current SpaceMouse family

        private readonly Action<string> _log;
        private readonly bool _verbose;
        private readonly object _lock = new object();
        private readonly SpaceMouseState _shared = new SpaceMouseState();
        private readonly SpaceMouseState _threadState = new SpaceMouseState();
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly Dictionary<IntPtr, bool> _acceptedDevices = new Dictionary<IntPtr, bool>();

        private Thread _thread;
        private IntPtr _hwnd;
        private WndProcDelegate _wndProc; // kept alive for the lifetime of the window
        private IntPtr _buffer;
        private int _bufferSize;
        private byte[] _report = new byte[64];
        private long _lastReportTicks = -1;
        private long _lastVerboseTicks;
        private volatile bool _running;

        public string DeviceName { get; private set; }
        public bool DeviceSeen { get; private set; }

        public RawInputReader(Action<string> log, bool verbose)
        {
            _log = log ?? (_ => { });
            _verbose = verbose;
        }

        public void Start()
        {
            if (_thread != null)
            {
                return;
            }
            _running = true;
            _thread = new Thread(ThreadMain)
            {
                IsBackground = true,
                Name = "StudioSpaceMouse.RawInput",
            };
            _thread.Start();
        }

        public void Stop()
        {
            _running = false;
            if (_hwnd != IntPtr.Zero)
            {
                PostMessage(_hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            }
            if (_thread != null)
            {
                _thread.Join(1000);
                _thread = null;
            }
        }

        public void Dispose()
        {
            Stop();
        }

        /// <summary>
        /// Copies the latest device state into <paramref name="into"/> and reports how
        /// long ago the last report arrived. Returns false if no report has been seen yet.
        /// </summary>
        public bool Snapshot(SpaceMouseState into, out double ageSeconds)
        {
            lock (_lock)
            {
                into.CopyFrom(_shared);
                if (_lastReportTicks < 0)
                {
                    ageSeconds = double.MaxValue;
                    return false;
                }
                ageSeconds = (_clock.ElapsedTicks - _lastReportTicks) / (double)Stopwatch.Frequency;
                return true;
            }
        }

        // ------------------------------------------------------------------ thread

        private void ThreadMain()
        {
            try
            {
                _wndProc = WndProc;
                string className = "StudioSpaceMouse.RawInput." + Process.GetCurrentProcess().Id;
                var wc = new WNDCLASSEX
                {
                    cbSize = Marshal.SizeOf(typeof(WNDCLASSEX)),
                    lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
                    hInstance = GetModuleHandle(null),
                    lpszClassName = className,
                };
                if (RegisterClassEx(ref wc) == 0)
                {
                    _log("RegisterClassEx failed: " + Marshal.GetLastWin32Error());
                    return;
                }

                _hwnd = CreateWindowEx(0, className, "StudioSpaceMouse", 0, 0, 0, 0, 0,
                    IntPtr.Zero, IntPtr.Zero, wc.hInstance, IntPtr.Zero);
                if (_hwnd == IntPtr.Zero)
                {
                    _log("CreateWindowEx failed: " + Marshal.GetLastWin32Error());
                    return;
                }

                var rid = new RAWINPUTDEVICE
                {
                    usUsagePage = 0x01,
                    usUsage = 0x08, // multi-axis controller
                    dwFlags = RIDEV_INPUTSINK | RIDEV_DEVNOTIFY,
                    hwndTarget = _hwnd,
                };
                if (!RegisterRawInputDevices(new[] { rid }, 1, Marshal.SizeOf(typeof(RAWINPUTDEVICE))))
                {
                    _log("RegisterRawInputDevices failed: " + Marshal.GetLastWin32Error());
                    return;
                }

                LogExistingDevices();

                MSG msg;
                while (_running && GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
                {
                    TranslateMessage(ref msg);
                    DispatchMessage(ref msg);
                }
            }
            catch (Exception ex)
            {
                _log("Raw input thread died: " + ex);
            }
            finally
            {
                if (_buffer != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(_buffer);
                    _buffer = IntPtr.Zero;
                }
                _hwnd = IntPtr.Zero;
            }
        }

        private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            switch (msg)
            {
                case WM_INPUT:
                    try
                    {
                        HandleInput(lParam);
                    }
                    catch (Exception ex)
                    {
                        _log("WM_INPUT handling failed: " + ex.Message);
                    }
                    break; // fall through to DefWindowProc so the system can clean up

                case WM_INPUT_DEVICE_CHANGE:
                    OnDeviceChange(wParam.ToInt32(), lParam);
                    return IntPtr.Zero;

                case WM_DESTROY:
                    PostQuitMessage(0);
                    return IntPtr.Zero;
            }
            return DefWindowProc(hWnd, msg, wParam, lParam);
        }

        private void HandleInput(IntPtr hRawInput)
        {
            int headerSize = Marshal.SizeOf(typeof(RAWINPUTHEADER));
            uint size = 0;
            GetRawInputData(hRawInput, RID_INPUT, IntPtr.Zero, ref size, (uint)headerSize);
            if (size == 0)
            {
                return;
            }
            EnsureBuffer((int)size);
            if (GetRawInputData(hRawInput, RID_INPUT, _buffer, ref size, (uint)headerSize) != size)
            {
                return;
            }

            int type = Marshal.ReadInt32(_buffer, 0);
            if (type != RIM_TYPEHID)
            {
                return;
            }
            IntPtr hDevice = Marshal.ReadIntPtr(_buffer, 8);
            if (!IsAccepted(hDevice))
            {
                return;
            }

            int sizeHid = Marshal.ReadInt32(_buffer, headerSize);
            int count = Marshal.ReadInt32(_buffer, headerSize + 4);
            int offset = headerSize + 8;
            if (sizeHid <= 0 || count <= 0)
            {
                return;
            }
            if (_report.Length < sizeHid)
            {
                _report = new byte[sizeHid];
            }

            bool any = false;
            for (int i = 0; i < count; i++)
            {
                Marshal.Copy(new IntPtr(_buffer.ToInt64() + offset), _report, 0, sizeHid);
                offset += sizeHid;
                if (HidReportParser.TryParse(_report, sizeHid, _threadState))
                {
                    any = true;
                    if (_verbose)
                    {
                        VerboseLog(sizeHid);
                    }
                }
                else if (_verbose)
                {
                    // Unknown report id: log it raw so new report formats can be identified.
                    _log(string.Format("unhandled report id={0} len={1} raw={2}",
                        _report[0], sizeHid, BitConverter.ToString(_report, 0, Math.Min(sizeHid, 24))));
                }
            }

            if (any)
            {
                lock (_lock)
                {
                    _shared.CopyFrom(_threadState);
                    _lastReportTicks = _clock.ElapsedTicks;
                }
            }
        }

        private int _reportsSinceVerboseLog;

        private void VerboseLog(int sizeHid)
        {
            // Buttons always log; axes are throttled to a few lines per second, with the
            // number of axis reports that arrived since the previous line (shows the report rate).
            long now = _clock.ElapsedTicks;
            bool isButtons = _report[0] == HidReportParser.ReportButtons;
            if (!isButtons)
            {
                _reportsSinceVerboseLog++;
                if ((now - _lastVerboseTicks) < Stopwatch.Frequency / 4)
                {
                    return;
                }
            }
            double elapsed = (now - _lastVerboseTicks) / (double)Stopwatch.Frequency;
            _lastVerboseTicks = now;
            _log(string.Format("report id={0} len={1} raw={2} | T=({3},{4},{5}) R=({6},{7},{8}) buttons=0x{9:X} | {10} axis reports in {11:F2}s",
                _report[0], sizeHid, BitConverter.ToString(_report, 0, Math.Min(sizeHid, 16)),
                _threadState.Tx, _threadState.Ty, _threadState.Tz,
                _threadState.Rx, _threadState.Ry, _threadState.Rz,
                _threadState.Buttons, _reportsSinceVerboseLog, elapsed));
            _reportsSinceVerboseLog = 0;
        }

        private bool IsAccepted(IntPtr hDevice)
        {
            bool accepted;
            if (_acceptedDevices.TryGetValue(hDevice, out accepted))
            {
                return accepted;
            }
            accepted = false;
            var info = new RID_DEVICE_INFO { cbSize = Marshal.SizeOf(typeof(RID_DEVICE_INFO)) };
            uint infoSize = (uint)info.cbSize;
            IntPtr p = Marshal.AllocHGlobal(info.cbSize);
            try
            {
                Marshal.StructureToPtr(info, p, false);
                if (GetRawInputDeviceInfo(hDevice, RIDI_DEVICEINFO, p, ref infoSize) > 0)
                {
                    info = (RID_DEVICE_INFO)Marshal.PtrToStructure(p, typeof(RID_DEVICE_INFO));
                    if (info.dwType == RIM_TYPEHID && Is3DconnexionDevice(info.hid.dwVendorId, info.hid.dwProductId))
                    {
                        accepted = true;
                        DeviceSeen = true;
                        DeviceName = string.Format("VID {0:X4} PID {1:X4} ({2})",
                            info.hid.dwVendorId, info.hid.dwProductId, GetDeviceName(hDevice));
                        _log("Using device " + DeviceName);
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(p);
            }
            _acceptedDevices[hDevice] = accepted;
            return accepted;
        }

        private static bool Is3DconnexionDevice(int vendorId, int productId)
        {
            if (vendorId == Vendor3Dconnexion)
            {
                return true;
            }
            if (vendorId != VendorLogitech)
            {
                return false;
            }
            // Logitech-era 3Dconnexion products only; other Logitech devices share the vendor id.
            switch (productId)
            {
                case 0xC603: // SpaceMouse Plus XT
                case 0xC605: // CadMan
                case 0xC606: // SpaceMouse Classic
                case 0xC621: // SpaceBall 5000
                case 0xC623: // SpaceTraveler
                case 0xC625: // SpacePilot
                case 0xC626: // SpaceNavigator
                case 0xC627: // SpaceExplorer
                case 0xC628: // SpaceNavigator for Notebooks
                case 0xC629: // SpacePilot Pro
                case 0xC62B: // SpaceMouse Pro
                    return true;
                default:
                    return false;
            }
        }

        private void OnDeviceChange(int what, IntPtr hDevice)
        {
            if (what == GIDC_ARRIVAL)
            {
                _acceptedDevices.Remove(hDevice);
                if (IsAccepted(hDevice))
                {
                    _log("Device connected.");
                }
            }
            else if (what == GIDC_REMOVAL)
            {
                bool wasAccepted;
                if (_acceptedDevices.TryGetValue(hDevice, out wasAccepted) && wasAccepted)
                {
                    _log("Device disconnected.");
                    _threadState.ClearAxes();
                    _threadState.Buttons = 0;
                    lock (_lock)
                    {
                        _shared.CopyFrom(_threadState);
                        _lastReportTicks = _clock.ElapsedTicks;
                    }
                }
                _acceptedDevices.Remove(hDevice);
            }
        }

        private void LogExistingDevices()
        {
            uint count = 0;
            int listItemSize = Marshal.SizeOf(typeof(RAWINPUTDEVICELIST));
            if (GetRawInputDeviceList(IntPtr.Zero, ref count, (uint)listItemSize) != 0 || count == 0)
            {
                _log("No raw input devices enumerated.");
                return;
            }
            IntPtr list = Marshal.AllocHGlobal((int)(count * listItemSize));
            try
            {
                uint got = GetRawInputDeviceList(list, ref count, (uint)listItemSize);
                int found = 0;
                for (int i = 0; i < got; i++)
                {
                    var item = (RAWINPUTDEVICELIST)Marshal.PtrToStructure(
                        new IntPtr(list.ToInt64() + i * listItemSize), typeof(RAWINPUTDEVICELIST));
                    if (item.dwType == RIM_TYPEHID && IsAccepted(item.hDevice))
                    {
                        found++;
                    }
                }
                if (found == 0)
                {
                    _log("No 3Dconnexion device present right now; will pick one up when it is plugged in.");
                }
            }
            finally
            {
                Marshal.FreeHGlobal(list);
            }
        }

        private string GetDeviceName(IntPtr hDevice)
        {
            uint size = 0;
            GetRawInputDeviceInfo(hDevice, RIDI_DEVICENAME, IntPtr.Zero, ref size);
            if (size == 0)
            {
                return "?";
            }
            IntPtr p = Marshal.AllocHGlobal((int)size * 2);
            try
            {
                if (GetRawInputDeviceInfo(hDevice, RIDI_DEVICENAME, p, ref size) > 0)
                {
                    return Marshal.PtrToStringUni(p);
                }
                return "?";
            }
            finally
            {
                Marshal.FreeHGlobal(p);
            }
        }

        private void EnsureBuffer(int size)
        {
            if (_buffer != IntPtr.Zero && _bufferSize >= size)
            {
                return;
            }
            if (_buffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_buffer);
            }
            _bufferSize = Math.Max(size, 256);
            _buffer = Marshal.AllocHGlobal(_bufferSize);
        }

        // ------------------------------------------------------------------ Win32

        private const uint WM_DESTROY = 0x0002;
        private const uint WM_CLOSE = 0x0010;
        private const uint WM_INPUT = 0x00FF;
        private const uint WM_INPUT_DEVICE_CHANGE = 0x00FE;
        private const int GIDC_ARRIVAL = 1;
        private const int GIDC_REMOVAL = 2;
        private const uint RID_INPUT = 0x10000003;
        private const uint RIDI_DEVICENAME = 0x20000007;
        private const uint RIDI_DEVICEINFO = 0x2000000B;
        private const int RIM_TYPEHID = 2;
        private const uint RIDEV_INPUTSINK = 0x00000100;
        private const uint RIDEV_DEVNOTIFY = 0x00002000;

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WNDCLASSEX
        {
            public int cbSize;
            public uint style;
            public IntPtr lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            [MarshalAs(UnmanagedType.LPWStr)] public string lpszMenuName;
            [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
            public IntPtr hIconSm;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public int ptX;
            public int ptY;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RAWINPUTDEVICE
        {
            public ushort usUsagePage;
            public ushort usUsage;
            public uint dwFlags;
            public IntPtr hwndTarget;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RAWINPUTHEADER
        {
            public int dwType;
            public int dwSize;
            public IntPtr hDevice;
            public IntPtr wParam;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RAWINPUTDEVICELIST
        {
            public IntPtr hDevice;
            public int dwType;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RID_DEVICE_INFO_HID
        {
            public int dwVendorId;
            public int dwProductId;
            public int dwVersionNumber;
            public ushort usUsagePage;
            public ushort usUsage;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct RID_DEVICE_INFO
        {
            [FieldOffset(0)] public int cbSize;
            [FieldOffset(4)] public int dwType;
            [FieldOffset(8)] public RID_DEVICE_INFO_HID hid;
            // The mouse/keyboard union members are larger than the HID one; pad to the real size (32 bytes).
            [FieldOffset(28)] private int _pad;
        }

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateWindowEx(uint dwExStyle, string lpClassName, string lpWindowName,
            uint dwStyle, int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu,
            IntPtr hInstance, IntPtr lpParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern void PostQuitMessage(int nExitCode);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

        [DllImport("user32.dll")]
        private static extern bool TranslateMessage(ref MSG lpMsg);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr DispatchMessage(ref MSG lpMsg);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] pRawInputDevices, uint uiNumDevices, int cbSize);

        [DllImport("user32.dll")]
        private static extern uint GetRawInputData(IntPtr hRawInput, uint uiCommand, IntPtr pData, ref uint pcbSize, uint cbSizeHeader);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern uint GetRawInputDeviceInfo(IntPtr hDevice, uint uiCommand, IntPtr pData, ref uint pcbSize);

        [DllImport("user32.dll")]
        private static extern uint GetRawInputDeviceList(IntPtr pRawInputDeviceList, ref uint puiNumDevices, uint cbSize);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);
    }
}
