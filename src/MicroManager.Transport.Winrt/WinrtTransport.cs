using System.Runtime.InteropServices;
using System.Text;
using MicroManager.Core.Transport;

namespace MicroManager.Transport.Winrt;

/// <summary>
/// Windows raw-HID transport using P/Invoke to hid.dll + setupapi.
///
/// Opens the vendor collection (usage page 0xFF00 / usage 0x01) and sends/receives
/// 64-byte raw-HID reports carrying the vendor JSON-RPC protocol.
///
/// This is the same transport approach verified by MicroManager.HidSpike against
/// a real Creator Micro 2 (VID 0x303A / PID 0x8298, firmware 0.6.3).
/// </summary>
public sealed class WinrtTransport : IWlTransport, IDisposable
{
    private IntPtr _handle = IntPtr.Zero;
    private readonly TaskCompletionSource<bool> _connectTcs = new();

    public string? Product => null; // P/Invoke transport does not expose product name
    public int? ProductId => null;  // would require reading device properties
    public string? Transport => "win32-hid";
    public string? Serial => null;
    public bool IsConnected => _handle != IntPtr.Zero;

    public event EventHandler<byte[]>? OnReportReceived;
    public event EventHandler<string>? OnDeviceLog;
    public event EventHandler<string>? OnDisconnect;

    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_handle != IntPtr.Zero)
            return Task.CompletedTask;

        var path = FindDevicePath();
        if (path is null)
            throw new DeviceNotConnectedException();

        _handle = Native.CreateFile(
            path,
            Native.GENERIC_READ | Native.GENERIC_WRITE,
            Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE,
            IntPtr.Zero,
            Native.OPEN_EXISTING,
            0,
            IntPtr.Zero);

        if (_handle == IntPtr.Zero || _handle.ToInt64() == -1)
        {
            _handle = IntPtr.Zero;
            throw new DeviceNotConnectedException();
        }

        _ = Task.Run(() => ReadLoop(cancellationToken), cancellationToken);
        return Task.CompletedTask;
    }

    public void Disconnect()
    {
        if (_handle != IntPtr.Zero)
        {
            Native.CloseHandle(_handle);
            _handle = IntPtr.Zero;
        }
        OnDisconnect?.Invoke(this, string.Empty);
    }

    public Task SendReportAsync(byte[] report, CancellationToken cancellationToken = default)
    {
        if (_handle == IntPtr.Zero)
            throw new DeviceNotConnectedException();

        var written = 0;
        var ok = Native.WriteFile(_handle, report, report.Length, out written, IntPtr.Zero);
        if (!ok)
            throw new IOException($"WriteFile failed: {Marshal.GetLastWin32Error()}");

        return Task.CompletedTask;
    }

    private async Task ReadLoop(CancellationToken cancellationToken)
    {
        var buffer = new byte[WireProtocol.ReportSize];
        try
        {
            while (!cancellationToken.IsCancellationRequested && _handle != IntPtr.Zero)
            {
                var read = 0;
                var ok = Native.ReadFile(_handle, buffer, buffer.Length, out read, IntPtr.Zero);
                if (!ok || read == 0)
                {
                    await Task.Delay(50, cancellationToken);
                    continue;
                }

                var text = Encoding.UTF8.GetString(buffer, 0, read);
                OnReportReceived?.Invoke(this, buffer);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            OnDeviceLog?.Invoke(this, ex.Message);
        }
    }

    private static string? FindDevicePath()
    {
        Native.HidD_GetHidGuid(out var hidGuid);
        var infoSet = Native.SetupDiGetClassDevs(
            ref hidGuid, null, IntPtr.Zero,
            Native.DIGCF_PRESENT | Native.DIGCF_DEVICEINTERFACE);

        if (infoSet == IntPtr.Zero || infoSet.ToInt64() == -1)
            return null;

        try
        {
            var memberIndex = 0;
            while (true)
            {
                var ifaceData = new SP_DEVICE_INTERFACE_DATA { Size = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
                if (!Native.SetupDiEnumDeviceInterfaces(infoSet, IntPtr.Zero, ref hidGuid, memberIndex, ref ifaceData))
                    break;

                // First call returns the required buffer size in `requiredSize` and is
                // expected to fail with ERROR_INSUFFICIENT_BUFFER (122). Treat any
                // other failure as a skip, and do NOT skip on the expected failure.
                var sized = Native.SetupDiGetDeviceInterfaceDetail(
                    infoSet, ref ifaceData, IntPtr.Zero, 0, out var requiredSize, IntPtr.Zero);
                if (!sized && Marshal.GetLastWin32Error() != Native.ERROR_INSUFFICIENT_BUFFER)
                {
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
                        memberIndex++;
                        continue;
                    }

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
                        memberIndex++;
                        continue;
                    }

                    try
                    {
                        var attrs = new HIDD_ATTRIBUTES { Size = Marshal.SizeOf<HIDD_ATTRIBUTES>() };
                        if (!Native.HidD_GetAttributes(handle, out attrs))
                        {
                            memberIndex++;
                            continue;
                        }

                        if (attrs.VendorID != WireProtocol.VendorIdWorkLouder)
                        {
                            memberIndex++;
                            continue;
                        }

                        if (!Native.HidD_GetPreparsedData(handle, out var preparsed) || preparsed == IntPtr.Zero)
                        {
                            memberIndex++;
                            continue;
                        }

                        try
                        {
                            if (Native.HidP_GetCaps(preparsed, out var caps) != 0 &&
                                caps.UsagePage == WireProtocol.VendorUsagePage &&
                                caps.Usage == WireProtocol.VendorUsage)
                            {
                                return path;
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

                    memberIndex++;
                }
                finally
                {
                    Marshal.FreeHGlobal(detailBuf);
                }
            }
        }
        finally
        {
            Native.SetupDiDestroyDeviceInfoList(infoSet);
        }

        return null;
    }

    public void Dispose()
    {
        Disconnect();
        GC.SuppressFinalize(this);
    }
}
