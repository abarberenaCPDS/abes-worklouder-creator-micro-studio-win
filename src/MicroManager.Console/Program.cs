using MicroManager.Core.Emulator;
using MicroManager.Core.Keymap;
using MicroManager.Core.Pad;
using MicroManager.Core.Transport;
using MicroManager.Transport.Winrt;

Console.WriteLine("=== Micro Manager Console Test ===");
Console.WriteLine();

// Prefer the real Creator Micro 2 over raw HID; fall back to the in-process
// emulator when no device is attached (same policy as the MAUI app).
RpcDevice rpcDevice;
if (Environment.GetEnvironmentVariable("MICROMANAGER_EMULATOR") is "1" or "true" or "True" or "TRUE")
{
    rpcDevice = new RpcDevice(new PadEmulator());
    await rpcDevice.ConnectAsync();
    Console.WriteLine($"Connected via {rpcDevice.Transport} (forced by MICROMANAGER_EMULATOR)");
}
else
{
    var real = new RpcDevice(new WinrtTransport());
    try
    {
        await real.ConnectAsync();
        rpcDevice = real;
        Console.WriteLine($"Connected via {rpcDevice.Transport}");
    }
    catch (DeviceNotConnectedException)
    {
        real.Disconnect();
        Console.WriteLine("Creator Micro 2 not found; using pad emulator.");
        rpcDevice = new RpcDevice(new PadEmulator());
        await rpcDevice.ConnectAsync();
        Console.WriteLine($"Connected via {rpcDevice.Transport}");
    }
}

// Every request gets a 5s budget so a silent device can never hang the harness.
async Task CallAsync(string method, object? parameters)
{
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    try
    {
        var response = await rpcDevice.RequestAsync(method, parameters, cts.Token);
        Console.WriteLine($"  {method}: {response.Result}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  {method} failed: {ex.GetType().Name}: {ex.Message}");
    }
}

Console.WriteLine("\n--- device.status ---");
await CallAsync("device.status", null);

Console.WriteLine("\n--- sys.version ---");
await CallAsync("sys.version", null);

Console.WriteLine("\n--- v.oai.thstatus (lighting) ---");
var threads = new List<Oai.Thread>
{
    new Oai.Thread(1) { Color = 0x00C853, Brightness = 1, Effect = Oai.Effect.Solid },
    new Oai.Thread(2) { Color = 0xFFA000, Brightness = 0.5, Effect = Oai.Effect.Solid },
};
await CallAsync("v.oai.thstatus", Oai.ThreadsParams(threads));

Console.WriteLine("\n--- Keymap binding ---");
try
{
    var changed = await KeymapManager.ApplyAsync(rpcDevice);
    Console.WriteLine($"  Keymap applied: {changed}");
}
catch (Exception ex)
{
    Console.WriteLine($"  Keymap apply failed: {ex.Message}");
}

await rpcDevice.DisposeAsync();
Console.WriteLine("\nDone!");
