using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Avalonia.Win32;

// ReSharper disable All
#pragma warning disable 649 // Unused variable

namespace rgit;

[SuppressMessage("StyleCop.CSharp.NamingRules", "SA1310:Field names should not contain underscore", Justification = "Names match win32 apis.")]
[SuppressMessage("StyleCop.CSharp.NamingRules", "SA1307:Accessible fields should begin with upper-case letter", Justification = "Names match win32 apis.")]
[SuppressMessage("Minor Code Smell", "S3459:Unassigned members should be removed", Justification = "Win32 apis.")]
[SuppressMessage("Minor Code Smell", "S3963:\"static\" fields should be initialized inline", Justification = "Initialization is complicated and only runs on Windows.")]
[SuppressMessage("Minor Code Smell", "S1450:Private fields only used as local variables in methods should become local variables", Justification = "This code was hard fought, don't care about lingering items.")]
[SuppressMessage("Major Code Smell", "S4200:Native methods should be wrapped", Justification = "Don't care.")]
public static class DarkMode
{
    private const int WH_CALLWNDPROC = 4;
    private const int WH_CALLWNDPROCRET = 12;

    private const uint RDW_INVALIDATE = 0x1;
    private const uint RDW_VALIDATE = 0x8;
    private const uint RDW_UPDATENOW = 0x100;
    private const uint RDW_FRAME = 0x0400;

    private const int GWL_STYLE = -16;
    private const int GWL_EXSTYLE = -20;

    private const int ATTACH_PARENT_PROCESS = -1;
    private const int STD_INPUT_HANDLE = -10;
    private const int STD_OUTPUT_HANDLE = -11;
    private const int STD_ERROR_HANDLE = -12;

    private static readonly bool DarkModeSupported = false;
    private static readonly uint BuildNumber;

    private static bool darkModeEnabled = false;

    private static bool CheckBuildNumber(uint buildNumber)
    {
        return buildNumber == 17763 // 1809
            || buildNumber == 18362 // 1903
            || buildNumber == 18363 // 1909
            || buildNumber == 19041 // 2004
            || buildNumber == 19044;
    }

    static DarkMode()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;

        // The bulk of this code comes from https://github.com/ysc3839/win32-darkmode
        RtlGetNtVersionNumbers(out var major, out var minor, out var buildNumber);
        buildNumber &= ~0xF0000000u;
        DarkMode.BuildNumber = buildNumber;

        if (major != 10 || minor != 0 || !CheckBuildNumber(buildNumber))
            return;

        const int LOAD_LIBRARY_SEARCH_SYSTEM32 = 0x00001000;
        var hUxtheme = LoadLibraryEx("uxtheme.dll", IntPtr.Zero, LOAD_LIBRARY_SEARCH_SYSTEM32);
        if (hUxtheme == IntPtr.Zero)
            return;

        OpenNcThemeDataCallback = Marshal.GetDelegateForFunctionPointer<OpenNcThemeDataDelegate>(GetProcAddress(hUxtheme, 49));
        RefreshImmersiveColorPolicyStateCallback = Marshal.GetDelegateForFunctionPointer<RefreshImmersiveColorPolicyStateDelegate>(GetProcAddress(hUxtheme, 104));
#pragma warning disable S1481 // Unused variable
        var getIsImmersiveColorUsingHighContrast = GetProcAddress(hUxtheme, 106);
#pragma warning restore S1481
        ShouldAppUseDarkCallback = Marshal.GetDelegateForFunctionPointer<ShouldAppUseDarkModeDelegate>(GetProcAddress(hUxtheme, 132));
        AllowDarkModeForWindowCallback = Marshal.GetDelegateForFunctionPointer<AllowDarkModeForWindowDelegate>(GetProcAddress(hUxtheme, 133));

        var ord135 = GetProcAddress(hUxtheme, 135);

        if (ord135 == IntPtr.Zero)
            return;

        if (buildNumber < 18362)
            AllowDarkModeForAppCallback = Marshal.GetDelegateForFunctionPointer<AllowDarkModeForAppDelegate>(ord135);
        else
            SetPreferredAppModeCallback = Marshal.GetDelegateForFunctionPointer<SetPreferredAppModeDelegate>(ord135);

        IsDarkModeAllowedForWindowCallback = Marshal.GetDelegateForFunctionPointer<IsDarkModeAllowedForWindowDelegate>(GetProcAddress(hUxtheme, 137));

        DarkModeSupported = true;

        AllowDarkModeForApp(true);
        RefreshImmersiveColorPolicyStateCallback();

        darkModeEnabled = ShouldAppUseDarkCallback() && !IsHighContrast();

        // If there's a bad scroll bar, fix it here
    }

    public static void EnableConsole()
    {
        // Attach to a console, if available.
        var attached = AttachConsole();

        if (!attached)
            return;

        // Set stderr to stdout, if available.
        var stdoutHandle = GetStdHandle(STD_OUTPUT_HANDLE);
        SetStdHandle(STD_ERROR_HANDLE, stdoutHandle);
    }

    private static void AllowDarkModeForApp(bool allow)
    {
        if (AllowDarkModeForAppCallback != null)
            AllowDarkModeForAppCallback(allow);
        else if (SetPreferredAppModeCallback != null)
            SetPreferredAppModeCallback(allow ? PreferredAppMode.AllowDark : PreferredAppMode.Default);
    }

    private struct HIGHCONTRASTW
    {
        public uint cbSize;
        public uint dwFlags;
        public IntPtr lpszDefaultScheme;
    }

    private static bool IsHighContrast()
    {
        const int SPI_GETHIGHCONTRAST = 0x0042;
        const int HCF_HIGHCONTRASTON = 0x00000001;
        var highContrast = new HIGHCONTRASTW { cbSize = (uint)Marshal.SizeOf<HIGHCONTRASTW>() };
        if (SystemParametersInfo(SPI_GETHIGHCONTRAST, highContrast.cbSize, ref highContrast, 0))
            return (highContrast.dwFlags & HCF_HIGHCONTRASTON) != 0;
        return false;
    }

    private static void SetDarkTheme(IntPtr hWnd)
    {
        // "Explorer" might work too
        SetWindowTheme(hWnd, "DarkMode_Explorer", null);
    }

    private static unsafe void RefreshTitleBarThemeColor(IntPtr hWnd)
    {
        var dark = IsDarkModeAllowedForWindowCallback(hWnd)
            && ShouldAppUseDarkCallback()
            && !IsHighContrast();
        if (BuildNumber < 18362)
        {
            SetProp(hWnd, "UseImmersiveDarkModeColors", (IntPtr)(dark ? 1 : 0));
        }
        else
        {
            var darkStack = stackalloc int[1];
            darkStack[0] = dark ? (byte)1 : (byte)0;
            var data = new WINDOWCOMPOSITIONATTRIBDATA
            {
                Attrib = WINDOWCOMPOSITIONATTRIB.WCA_USEDARKMODECOLORS,
                pvData = (IntPtr)darkStack,
                cbData = (UIntPtr)sizeof(int),
            };

            var pData = &data;
            SetWindowCompositionAttribute(hWnd, (IntPtr)pData);
        }
    }

    private enum WINDOWCOMPOSITIONATTRIB
    {
        WCA_UNDEFINED = 0,
        WCA_NCRENDERING_ENABLED = 1,
        WCA_NCRENDERING_POLICY = 2,
        WCA_TRANSITIONS_FORCEDISABLED = 3,
        WCA_ALLOW_NCPAINT = 4,
        WCA_CAPTION_BUTTON_BOUNDS = 5,
        WCA_NONCLIENT_RTL_LAYOUT = 6,
        WCA_FORCE_ICONIC_REPRESENTATION = 7,
        WCA_EXTENDED_FRAME_BOUNDS = 8,
        WCA_HAS_ICONIC_BITMAP = 9,
        WCA_THEME_ATTRIBUTES = 10,
        WCA_NCRENDERING_EXILED = 11,
        WCA_NCADORNMENTINFO = 12,
        WCA_EXCLUDED_FROM_LIVEPREVIEW = 13,
        WCA_VIDEO_OVERLAY_ACTIVE = 14,
        WCA_FORCE_ACTIVEWINDOW_APPEARANCE = 15,
        WCA_DISALLOW_PEEK = 16,
        WCA_CLOAK = 17,
        WCA_CLOAKED = 18,
        WCA_ACCENT_POLICY = 19,
        WCA_FREEZE_REPRESENTATION = 20,
        WCA_EVER_UNCLOAKED = 21,
        WCA_VISUAL_OWNER = 22,
        WCA_HOLOGRAPHIC = 23,
        WCA_EXCLUDED_FROM_DDA = 24,
        WCA_PASSIVEUPDATEMODE = 25,
        WCA_USEDARKMODECOLORS = 26,
        WCA_LAST = 27,
    }

    private struct WINDOWCOMPOSITIONATTRIBDATA
    {
        public WINDOWCOMPOSITIONATTRIB Attrib;
        public IntPtr pvData;
        public UIntPtr cbData;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct IMAGE_DOS_HEADER
    {
        public fixed byte e_magic[2]; // Magic number

        public ushort e_cblp; // Bytes on last page of file
        public ushort e_cp; // Pages in file
        public ushort e_crlc; // Relocations
        public ushort e_cparhdr; // Size of header in paragraphs
        public ushort e_minalloc; // Minimum extra paragraphs needed
        public ushort e_maxalloc; // Maximum extra paragraphs needed
        public ushort e_ss; // Initial (relative) SS value
        public ushort e_sp; // Initial SP value
        public ushort e_csum; // Checksum
        public ushort e_ip; // Initial IP value
        public ushort e_cs; // Initial (relative) CS value
        public ushort e_lfarlc; // File address of relocation table
        public ushort e_ovno; // Overlay number

        public fixed ushort e_res1[4]; // Reserved words

        public ushort e_oemid; // OEM identifier (for e_oeminfo)
        public ushort e_oeminfo; // OEM information; e_oemid specific

        public fixed ushort e_res2[10]; // Reserved words

        public int e_lfanew; // File address of new exe header
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct IMAGE_FILE_HEADER
    {
        public ushort Machine;
        public ushort NumberOfSections;
        public uint TimeDateStamp;
        public uint PointerToSymbolTable;
        public uint NumberOfSymbols;
        public ushort SizeOfOptionalHeader;
        public ushort Characteristics;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct IMAGE_NT_HEADERS64
    {
        [FieldOffset(0)]
        public uint Signature;

        [FieldOffset(4)]
        public IMAGE_FILE_HEADER FileHeader;

        [FieldOffset(24)]
        public IMAGE_OPTIONAL_HEADER64 OptionalHeader;
    }

    private struct IMAGE_DELAYLOAD_DESCRIPTOR
    {
        public uint AllAttributes;
        public uint DllNameRVA; // RVA to the name of the target library (NULL-terminate ASCII string)
        public uint ModuleHandleRVA; // RVA to the HMODULE caching location (PHMODULE)
        public uint ImportAddressTableRVA; // RVA to the start of the IAT (PIMAGE_THUNK_DATA)
        public uint ImportNameTableRVA; // RVA to the start of the name table (PIMAGE_THUNK_DATA::AddressOfData)
        public uint BoundImportAddressTableRVA; // RVA to an optional bound IAT
        public uint UnloadInformationTableRVA; // RVA to an optional unload info table

        // 0 if not bound,
        // Otherwise, date/time of the target DLL
        public uint TimeDateStamp;
    }

    private enum MachineType : ushort
    {
        Native = 0,
        I386 = 0x014c,
        Itanium = 0x0200,
        x64 = 0x8664,
    }

    private enum MagicType : ushort
    {
        IMAGE_NT_OPTIONAL_HDR32_MAGIC = 0x10b,
        IMAGE_NT_OPTIONAL_HDR64_MAGIC = 0x20b,
    }

    private enum SubSystemType : ushort
    {
        IMAGE_SUBSYSTEM_UNKNOWN = 0,
        IMAGE_SUBSYSTEM_NATIVE = 1,
        IMAGE_SUBSYSTEM_WINDOWS_GUI = 2,
        IMAGE_SUBSYSTEM_WINDOWS_CUI = 3,
        IMAGE_SUBSYSTEM_POSIX_CUI = 7,
        IMAGE_SUBSYSTEM_WINDOWS_CE_GUI = 9,
        IMAGE_SUBSYSTEM_EFI_APPLICATION = 10,
        IMAGE_SUBSYSTEM_EFI_BOOT_SERVICE_DRIVER = 11,
        IMAGE_SUBSYSTEM_EFI_RUNTIME_DRIVER = 12,
        IMAGE_SUBSYSTEM_EFI_ROM = 13,
        IMAGE_SUBSYSTEM_XBOX = 14,
    }

    private enum DllCharacteristicsType : ushort
    {
        RES_0 = 0x0001,
        RES_1 = 0x0002,
        RES_2 = 0x0004,
        RES_3 = 0x0008,
        IMAGE_DLL_CHARACTERISTICS_DYNAMIC_BASE = 0x0040,
        IMAGE_DLL_CHARACTERISTICS_FORCE_INTEGRITY = 0x0080,
        IMAGE_DLL_CHARACTERISTICS_NX_COMPAT = 0x0100,
        IMAGE_DLLCHARACTERISTICS_NO_ISOLATION = 0x0200,
        IMAGE_DLLCHARACTERISTICS_NO_SEH = 0x0400,
        IMAGE_DLLCHARACTERISTICS_NO_BIND = 0x0800,
        RES_4 = 0x1000,
        IMAGE_DLLCHARACTERISTICS_WDM_DRIVER = 0x2000,
        IMAGE_DLLCHARACTERISTICS_TERMINAL_SERVER_AWARE = 0x8000,
    }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct IMAGE_OPTIONAL_HEADER64
    {
        public MagicType Magic;
        public byte MajorLinkerVersion;
        public byte MinorLinkerVersion;
        public int SizeOfCode;
        public int SizeOfInitializedData;
        public int SizeOfUninitializedData;
        public int AddressOfEntryPoint;
        public int BaseOfCode;
        public ulong ImageBase;
        public int SectionAlignment;
        public int FileAlignment;
        public short MajorOperatingSystemVersion;
        public short MinorOperatingSystemVersion;
        public short MajorImageVersion;
        public short MinorImageVersion;
        public short MajorSubsystemVersion;
        public short MinorSubsystemVersion;
        public int Win32VersionValue;
        public int SizeOfImage;
        public int SizeOfHeaders;
        public int CheckSum;
        public short Subsystem;
        public short DllCharacteristics;
        public ulong SizeOfStackReserve;
        public ulong SizeOfStackCommit;
        public ulong SizeOfHeapReserve;
        public ulong SizeOfHeapCommit;
        public int LoaderFlags;
        public int NumberOfRvaAndSizes;
        public IMAGE_DATA_DIRECTORY DataDirectory; // An array of 16
    }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct IMAGE_DATA_DIRECTORY
    {
        public uint VirtualAddress;
        public uint Size;
    }

    private enum PreferredAppMode
    {
        Default,
        AllowDark,
        ForceDark,
        ForceLight,
        Max,
    }

    private delegate bool AllowDarkModeForWindowDelegate(IntPtr hWnd, bool allow);

    private delegate bool IsDarkModeAllowedForWindowDelegate(IntPtr hWnd);

    private delegate bool AllowDarkModeForAppDelegate(bool allow);

    private delegate PreferredAppMode SetPreferredAppModeDelegate(PreferredAppMode appMode);

    private delegate void RefreshImmersiveColorPolicyStateDelegate();

    private delegate bool ShouldAppUseDarkModeDelegate();

    private delegate IntPtr OpenNcThemeDataDelegate(IntPtr hWnd, IntPtr pszClassList);

#pragma warning disable SA1306
    private static AllowDarkModeForWindowDelegate AllowDarkModeForWindowCallback = null!;
    private static IsDarkModeAllowedForWindowDelegate IsDarkModeAllowedForWindowCallback = null!;
    private static AllowDarkModeForAppDelegate? AllowDarkModeForAppCallback;
    private static SetPreferredAppModeDelegate? SetPreferredAppModeCallback;
    private static RefreshImmersiveColorPolicyStateDelegate RefreshImmersiveColorPolicyStateCallback = null!;
    private static ShouldAppUseDarkModeDelegate ShouldAppUseDarkCallback = null!;
    private static OpenNcThemeDataDelegate OpenNcThemeDataCallback = null!;
#pragma warning restore

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref HIGHCONTRASTW pvParam, uint fWinIni); // T = any type

    [DllImport("kernel32", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, IntPtr lpProcName);

    [DllImport("kernel32", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

    private static IntPtr GetProcAddress(IntPtr hModule, int ordinal) => GetProcAddress(hModule, (IntPtr)ordinal);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryEx(string lpFileName, IntPtr hReservedNull, uint dwFlags);

    [DllImport("ntdll.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern void RtlGetNtVersionNumbers(out uint major, out uint minor, out uint buildNumber);

    [DllImport("UxTheme.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr SetWindowTheme(IntPtr hwnd, string pszSubAppName, string? pszSubIdList);

    [DllImport("User32.dll", CharSet = CharSet.Auto)]
    private static extern bool SetWindowCompositionAttribute(IntPtr hWnd, IntPtr pAttrData);

    [DllImport("User32.dll", CharSet = CharSet.Auto)]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("User32.dll")]
    private static extern IntPtr GetDCEx(IntPtr hWnd, IntPtr hrgnClip, int flags);

    [DllImport("User32.dll", SetLastError = true)]
    private static extern bool SetProp(IntPtr hWnd, string lpString, IntPtr hData);

    [DllImport("User32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookExA(int idHook, WndProcDelegate lpfn, IntPtr hmod = default, ushort dwThreadId = 0);

    [DllImport("User32.dll", SetLastError = true)]
    private static extern nint CallNextHookEx(IntPtr hhk, int nCode, nuint wParam, nuint lParam);

    [DllImport("User32.dll", SetLastError = true)]
    private static extern nint UnhookWindowsHookEx(IntPtr hook);

    [DllImport("kernel32.dll")]
    private static extern bool VirtualProtectEx(IntPtr hProcess, IntPtr lpAddress, UIntPtr dwSize, uint flNewProtect, out uint lpflOldProtect);

    [DllImport("User32.dll")]
    private static extern ushort GetWindowThreadProcessId(IntPtr hWnd, out ushort lpdwProcessId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandleA(string? lpModuleName);

    [DllImport("User32.dll", SetLastError = true)]
    private static extern nint SendMessageA(IntPtr hWnd, uint msg, nuint wParam, nuint lParam);

    [Flags]
    [SuppressMessage("Minor Code Smell", "S2344:Enumeration type names should not have \"Flags\" or \"Enum\" suffixes", Justification = "Win32.")]
    private enum SetWindowPosFlags : uint
    {
        /// <summary>
        ///     If the calling thread and the thread that owns the window are attached to different input queues, the system posts the request to the thread that owns the window. This prevents the calling thread from blocking its execution while other threads process the request.
        /// </summary>
        SWP_ASYNCWINDOWPOS = 0x4000,

        /// <summary>
        ///     Prevents generation of the WM_SYNCPAINT message.
        /// </summary>
        SWP_DEFERERASE = 0x2000,

        /// <summary>
        ///     Draws a frame (defined in the window's class description) around the window.
        /// </summary>
        SWP_DRAWFRAME = 0x0020,

        /// <summary>
        ///     Applies new frame styles set using the SetWindowLong function. Sends a WM_NCCALCSIZE message to the window, even if the window's size is not being changed. If this flag is not specified, WM_NCCALCSIZE is sent only when the window's size is being changed.
        /// </summary>
        SWP_FRAMECHANGED = 0x0020,

        /// <summary>
        ///     Hides the window.
        /// </summary>
        SWP_HIDEWINDOW = 0x0080,

        /// <summary>
        ///     Does not activate the window. If this flag is not set, the window is activated and moved to the top of either the topmost or non-topmost group (depending on the setting of the hWndInsertAfter parameter).
        /// </summary>
        SWP_NOACTIVATE = 0x0010,

        /// <summary>
        ///     Discards the entire contents of the client area. If this flag is not specified, the valid contents of the client area are saved and copied back into the client area after the window is sized or repositioned.
        /// </summary>
        SWP_NOCOPYBITS = 0x0100,

        /// <summary>
        ///     Retains the current position (ignores X and Y parameters).
        /// </summary>
        SWP_NOMOVE = 0x0002,

        /// <summary>
        ///     Does not change the owner window's position in the Z order.
        /// </summary>
        SWP_NOOWNERZORDER = 0x0200,

        /// <summary>
        ///     Does not redraw changes. If this flag is set, no repainting of any kind occurs. This applies to the client area, the nonclient area (including the title bar and scroll bars), and any part of the parent window uncovered as a result of the window being moved. When this flag is set, the application must explicitly invalidate or redraw any parts of the window and parent window that need redrawing.
        /// </summary>
        SWP_NOREDRAW = 0x0008,

        /// <summary>
        ///     Same as the SWP_NOOWNERZORDER flag.
        /// </summary>
        SWP_NOREPOSITION = 0x0200,

        /// <summary>
        ///     Prevents the window from receiving the WM_WINDOWPOSCHANGING message.
        /// </summary>
        SWP_NOSENDCHANGING = 0x0400,

        /// <summary>
        ///     Retains the current size (ignores the cx and cy parameters).
        /// </summary>
        SWP_NOSIZE = 0x0001,

        /// <summary>
        ///     Retains the current Z order (ignores the hWndInsertAfter parameter).
        /// </summary>
        SWP_NOZORDER = 0x0004,

        /// <summary>
        ///     Displays the window.
        /// </summary>
        SWP_SHOWWINDOW = 0x0040,
    }

    [DllImport("User32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("User32.dll", SetLastError = true)]
    private static extern bool UpdateWindow(IntPtr hWnd);

    [DllImport("User32.dll", SetLastError = true)]
    private static extern bool RedrawWindow(IntPtr hWnd, IntPtr lprcUpdate, IntPtr hrgnUpdate, uint flags);

    [DllImport("User32.dll", SetLastError = true)]
    private static extern IntPtr GetActiveWindow();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int dwProcessId = ATTACH_PARENT_PROCESS);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetStdHandle(int nStdHandle, IntPtr hHandle);

    private enum WindowsMessage
    {
        WM_CREATE = 0x0001,
        WM_SIZE = 0x0005,
        WM_ACTIVATE = 0x0006,
        WM_KILLFOCUS = 0x0008,
        WM_ACTIVATEAPP = 0x001C,
        WM_SETCURSOR = 0x0020,
        WM_STYLECHANGED = 0x007D,
        WM_NCHITTEST = 0x0084,
        WM_NCPAINT = 0x0085,
        WM_NCACTIVATE = 0x0086,
        WM_INITDIALOG = 0x0110,
        WM_CTLCOLORMSGBOX = 0x0132,
        WM_THEMECHANGED = 0x031A,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public UIntPtr wParam;
        public IntPtr lParam;
        public int time;
        public POINT pt;
        public int lPrivate;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CWPSTRUCT
    {
        public nuint lParam;
        public nuint wParam;
        public uint message;
        public IntPtr hwnd;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CWPRETSTRUCT
    {
        public nint lResult;
        public nuint lParam;
        public nuint wParam;
        public uint message;
        public IntPtr hwnd;
    }

    private delegate IntPtr WndProcDelegate(int nCode, nuint wParam, nuint lParam);

    private sealed class GCFuncHandle : IDisposable
    {
        private GCHandle handle;

        public GCFuncHandle(GCHandle handle)
        {
            this.handle = handle;
        }

        public GCFuncHandle(object? obj)
        {
            this.handle = GCHandle.Alloc(obj);
        }

        public GCFuncHandle(IntPtr ptr)
        {
            this.handle = GCHandle.FromIntPtr(ptr);
        }

        ~GCFuncHandle() => this.Dispose();

        public void Dispose()
        {
            GC.SuppressFinalize(this);
            this.handle.Free();
        }

        public static T CreateDelegate<T>(T d, out GCFuncHandle handle)
            where T : Delegate
        {
            handle = new GCFuncHandle(GCHandle.Alloc(d));
            return d;
        }
    }

    public class DarkModeWindow : WindowImpl
    {
        protected override IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            switch ((WindowsMessage)msg)
            {
                case WindowsMessage.WM_CREATE:
                {
                    if (DarkModeSupported)
                    {
                        AllowDarkModeForWindowCallback(hWnd, darkModeEnabled);
                        SetDarkTheme(hWnd);
                        RefreshTitleBarThemeColor(hWnd);
                    }

                    break;
                }
                case WindowsMessage.WM_INITDIALOG:
                {
                    if (DarkModeSupported)
                    {
                        SetDarkTheme(hWnd);
                        SendMessageA(hWnd, (uint)WindowsMessage.WM_THEMECHANGED, 0, 0);
                    }

                    break;
                }
                case WindowsMessage.WM_THEMECHANGED:
                {
                    AllowDarkModeForWindowCallback(hWnd, darkModeEnabled);
                    RefreshTitleBarThemeColor(hWnd);

                    break;
                }
                default:
                {
                    break;
                }
            }

            return base.WndProc(hWnd, msg, wParam, lParam);
        }
    }
}