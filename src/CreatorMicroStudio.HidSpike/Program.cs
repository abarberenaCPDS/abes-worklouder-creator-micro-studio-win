using System.Runtime.InteropServices;
using System.Text;

namespace AbesWorkLouderCreatorMicroStudio.Windows.HidSpike;

internal static class Native
{
    [DllImport("hid.dll", SetLastError = true)]
    public static extern void HidD_GetHidGuid(out Guid guid);

    [DllImport("hid.dll", SetLastError = true)]
    public static extern bool HidD_GetAttributes(IntPtr device, out HIDD_ATTRIBUTES attributes);

    [DllImport("hid.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern bool HidD_GetPreparsedData(IntPtr device, out IntPtr preparsedData);

    [DllImport("hid.dll", SetLastError = true)]
    public static extern bool HidD_FreePreparsedData(IntPtr preparsedData);

    [DllImport("hid.dll", SetLastError = true)]
    public static extern int HidP_GetCaps(IntPtr preparsedData, out HIDP_CAPS caps);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern IntPtr SetupDiGetClassDevs(
        ref Guid classGuid,
        string? enumerator,
        IntPtr hwndParent,
        int flags);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern bool SetupDiEnumDeviceInterfaces(
        IntPtr deviceInfoSet,
        IntPtr deviceInfoData,
        ref Guid interfaceClassGuid,
        int memberIndex,
        ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern bool SetupDiGetDeviceInterfaceDetail(
        IntPtr deviceInfoSet,
        ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData,
        IntPtr deviceInterfaceDetailData,
        int deviceInterfaceDetailDataSize,
        out int requiredSize,
        IntPtr deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    public static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern IntPtr CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool WriteFile(
        IntPtr hFile,
        byte[] lpBuffer,
        int nNumberOfBytesToWrite,
        out int lpNumberOfBytesWritten,
        IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool ReadFile(
        IntPtr hFile,
        byte[] lpBuffer,
        int nNumberOfBytesToRead,
        out int lpNumberOfBytesRead,
        IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr hObject);

    public const int DIGCF_PRESENT = 0x00000002;
    public const int DIGCF_DEVICEINTERFACE = 0x00000010;

    public const uint GENERIC_READ = 0x80000000;
    public const uint GENERIC_WRITE = 0x40000000;
    public const uint FILE_SHARE_READ = 0x00000001;
    public const uint FILE_SHARE_WRITE = 0x00000002;
    public const uint OPEN_EXISTING = 3;

    public const ushort VendorIdWorkLouder = 0x303A;
    public const ushort VendorUsagePage = 0xFF00;
    public const byte ReportId = 0x06;
    public const byte ChannelRpc = 2;
    public const int MaxChunk = 61;
    public const int ReportSize = 64;
}

[StructLayout(LayoutKind.Sequential)]
internal struct HIDD_ATTRIBUTES
{
    public int Size;
    public ushort VendorID;
    public ushort ProductID;
    public ushort VersionNumber;
}

[StructLayout(LayoutKind.Sequential)]
internal struct HIDP_CAPS
{
    public ushort Usage;
    public ushort UsagePage;
    public ushort InputReportByteLength;
    public ushort OutputReportByteLength;
    public ushort FeatureReportByteLength;
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
    public ushort[] Reserved;
}

[StructLayout(LayoutKind.Sequential)]
internal struct SP_DEVICE_INTERFACE_DATA
{
    public int Size;
    public Guid InterfaceClassGuid;
    public int Flags;
    public IntPtr Reserved;
}

internal record HidDeviceInfo(string Path, HIDD_ATTRIBUTES Attributes, HIDP_CAPS Caps);

internal static class Program
{
    private static Guid HidGuid;

    static Program()
    {
        Native.HidD_GetHidGuid(out HidGuid);
    }

    public static async Task Main()
    {
        Console.WriteLine("=== Abe's WorkLouder Creator Micro Studio · HID Probe ===");
        Console.WriteLine($"Work Louder VID: {Native.VendorIdWorkLouder:X4}, vendor usage page: {Native.VendorUsagePage:X4}");
        Console.WriteLine();

        var devices = EnumerateHidDevices();
        Console.WriteLine($"Total HID interfaces enumerated: {devices.Count}");

        var wlDevices = devices.Where(d => d.Attributes.VendorID == Native.VendorIdWorkLouder).ToList();
        Console.WriteLine($"Work Louder devices: {wlDevices.Count}");

        foreach (var dev in wlDevices)
        {
            var vendor = dev.Caps.UsagePage == Native.VendorUsagePage ? " <-- VENDOR COLLECTION" : "";
            Console.WriteLine($"  {dev.Path}");
            Console.WriteLine($"    PID: {dev.Attributes.ProductID:X4} UsagePage: {dev.Caps.UsagePage:X4} Usage: {dev.Caps.Usage:X4}" +
                $" InReport: {dev.Caps.InputReportByteLength} OutReport: {dev.Caps.OutputReportByteLength}{vendor}");
        }

        var target = wlDevices.FirstOrDefault(d => d.Caps.UsagePage == Native.VendorUsagePage)
                     ?? wlDevices.FirstOrDefault();

        if (target is null)
        {
            Console.WriteLine("No Work Louder device found. Is the pad plugged in and connected?");
            return;
        }

        Console.WriteLine();
        Console.WriteLine($"=== Probing: {target.Path} ===");
        await ProbeDeviceAsync(target.Path, target.Caps);
    }

    private static List<HidDeviceInfo> EnumerateHidDevices()
    {
        var result = new List<HidDeviceInfo>();
        var infoSet = Native.SetupDiGetClassDevs(
            ref HidGuid, null, IntPtr.Zero,
            Native.DIGCF_PRESENT | Native.DIGCF_DEVICEINTERFACE);

        if (infoSet == IntPtr.Zero || infoSet.ToInt64() == -1)
        {
            Console.WriteLine($"SetupDiGetClassDevs failed: {Marshal.GetLastWin32Error()}");
            return result;
        }

        try
        {
            var memberIndex = 0;
            while (true)
            {
                var ifaceData = new SP_DEVICE_INTERFACE_DATA { Size = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
                if (!Native.SetupDiEnumDeviceInterfaces(infoSet, IntPtr.Zero, ref HidGuid, memberIndex, ref ifaceData))
                    break;

                Console.WriteLine($"  [{memberIndex}] SetupDiEnumDeviceInterfaces OK");

                var detailOk = Native.SetupDiGetDeviceInterfaceDetail(
                    infoSet, ref ifaceData, IntPtr.Zero, 0, out var requiredSize, IntPtr.Zero);
                if (!detailOk && Marshal.GetLastWin32Error() != 122)
                {
                    Console.WriteLine($"  [{memberIndex}] SetupDiGetDeviceInterfaceDetail size failed: {Marshal.GetLastWin32Error()}");
                    memberIndex++;
                    continue;
                }

                var detailBuf = Marshal.AllocHGlobal(requiredSize);
                try
                {
                    Marshal.WriteInt32(detailBuf, IntPtr.Size == 8 ? 8 : 4 + Marshal.SystemDefaultCharSize * 2);

                    if (!Native.SetupDiGetDeviceInterfaceDetail(
                            infoSet, ref ifaceData, detailBuf, requiredSize, out _, IntPtr.Zero))
                    {
                        Console.WriteLine($"  [{memberIndex}] SetupDiGetDeviceInterfaceDetail detail failed: {Marshal.GetLastWin32Error()}");
                        memberIndex++;
                        continue;
                    }

                    // SP_DEVICE_INTERFACE_DETAIL_DATA: cbSize (4 bytes) then DevicePath.
                    // On 64-bit Windows some headers suggest cbSize=8 with padding,
                    // but the actual layout is cbSize@0, DevicePath@4.
                    var pathPtr = detailBuf + 4;
                    var path = Marshal.PtrToStringUni(pathPtr)!;

                    var handle = Native.CreateFile(
                        path,
                        Native.GENERIC_READ | Native.GENERIC_WRITE,
                        Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE,
                        IntPtr.Zero,
                        Native.OPEN_EXISTING,
                        0,
                        IntPtr.Zero);

                    if (handle == IntPtr.Zero || handle.ToInt64() == -1)
                    {
                        Console.WriteLine($"  [{memberIndex}] CreateFile failed: {Marshal.GetLastWin32Error()} path={path}");
                        memberIndex++;
                        continue;
                    }

                    try
                    {
                        var attrs = new HIDD_ATTRIBUTES { Size = Marshal.SizeOf<HIDD_ATTRIBUTES>() };
                        if (!Native.HidD_GetAttributes(handle, out attrs))
                        {
                            Console.WriteLine($"  [{memberIndex}] HidD_GetAttributes failed: {Marshal.GetLastWin32Error()}");
                            memberIndex++;
                            continue;
                        }

                        // Only probe Work Louder devices; HidD_GetPreparsedData can crash on
                        // non-WL HID interfaces (touchpads, fingerprint sensors, etc.).
                        if (attrs.VendorID != Native.VendorIdWorkLouder)
                        {
                            memberIndex++;
                            continue;
                        }

                        Console.WriteLine($"  [{memberIndex}] Found Work Louder device: VID={attrs.VendorID:X4} PID={attrs.ProductID:X4}");

                        if (!Native.HidD_GetPreparsedData(handle, out IntPtr preparsed))
                        {
                            Console.WriteLine($"  [{memberIndex}] HidD_GetPreparsedData failed: {Marshal.GetLastWin32Error()}");
                            memberIndex++;
                            continue;
                        }

                        try
                        {
                            var capsOk = Native.HidP_GetCaps(preparsed, out var caps);
                            if (capsOk != 0)
                            {
                                result.Add(new HidDeviceInfo(path, attrs, caps));
                                Console.WriteLine($"    UsagePage={caps.UsagePage:X4} Usage={caps.Usage:X4} In={caps.InputReportByteLength} Out={caps.OutputReportByteLength}");
                            }
                            else
                            {
                                Console.WriteLine($"    HidP_GetCaps=0");
                            }
                        }
                        finally
                        {
                            Native.HidD_FreePreparsedData(preparsed);
                        }
                    }
                    finally
                    {
                        Native.CloseHandle(handle);
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(detailBuf);
                }

                memberIndex++;
            }
        }
        finally
        {
            Native.SetupDiDestroyDeviceInfoList(infoSet);
        }

        return result;
    }

    private static async Task ProbeDeviceAsync(string path, HIDP_CAPS caps)
    {
        var handle = Native.CreateFile(
            path,
            Native.GENERIC_READ | Native.GENERIC_WRITE,
            Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE,
            IntPtr.Zero,
            Native.OPEN_EXISTING,
            0,
            IntPtr.Zero);

        if (handle == IntPtr.Zero || handle.ToInt64() == -1)
        {
            Console.WriteLine($"CreateFile failed: {Marshal.GetLastWin32Error()}");
            return;
        }

        try
        {
            var payload = Encoding.UTF8.GetBytes("{\"id\":1,\"method\":\"sys.version\",\"params\":{}}");
            var reports = EncodeReports(payload);
            Console.WriteLine($"Sending {reports.Length} report(s)...");

            foreach (var report in reports)
            {
                var written = 0;
                var ok = Native.WriteFile(handle, report, report.Length, out written, IntPtr.Zero);
                Console.WriteLine($"  WriteFile ok={ok} bytes={written} err={Marshal.GetLastWin32Error()}");
                if (!ok) return;
            }

            Console.WriteLine("Reading response (5s timeout)...");
            var buffer = new byte[Native.ReportSize];
            var accumulator = new List<byte>();
            var deadline = DateTime.UtcNow.AddSeconds(5);

            while (DateTime.UtcNow < deadline)
            {
                var read = 0;
                var ok = Native.ReadFile(handle, buffer, buffer.Length, out read, IntPtr.Zero);
                if (!ok || read == 0)
                {
                    await Task.Delay(50);
                    continue;
                }

                var start = buffer[0] == Native.ReportId ? 3 : 0;
                var len = start < buffer.Length ? buffer[start - 1] : (byte)0;
                for (var i = 0; i < len && start + i < read; i++)
                    accumulator.Add(buffer[start + i]);

                var text = Encoding.UTF8.GetString(accumulator.ToArray());
                if (text.Contains('}'))
                {
                    Console.WriteLine($"  Response: {text}");
                    return;
                }
            }

            if (accumulator.Count == 0)
                Console.WriteLine("  No response received within timeout.");
            else
                Console.WriteLine($"  Partial response: {Encoding.UTF8.GetString(accumulator.ToArray())}");
        }
        finally
        {
            Native.CloseHandle(handle);
        }
    }

    private static byte[][] EncodeReports(byte[] payload)
    {
        var reports = new List<byte[]>();
        var offset = 0;
        while (offset < payload.Length)
        {
            var chunk = Math.Min(Native.MaxChunk, payload.Length - offset);
            var report = new byte[Native.ReportSize];
            report[0] = Native.ReportId;
            report[1] = Native.ChannelRpc;
            report[2] = (byte)chunk;
            Array.Copy(payload, offset, report, 3, chunk);
            reports.Add(report);
            offset += chunk;
        }

        return reports.Count > 0 ? reports.ToArray() : new[] { new byte[Native.ReportSize] };
    }
}
