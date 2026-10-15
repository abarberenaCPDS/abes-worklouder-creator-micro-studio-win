using MicroManager.Core.Config;
using MicroManager.Core.Emulator;
using MicroManager.Core.Herdr;
using MicroManager.Core.Keymap;
using MicroManager.Core.Pad;
using MicroManager.Core.Transport;
using MicroManager.Transport.Winrt;

var emulator = new PadEmulator();
var transport = (IWlTransport)emulator;
var rpcDevice = new RpcDevice(transport);

Console.WriteLine("=== Micro Manager Console Test ===");
Console.WriteLine();

try
{
    await rpcDevice.ConnectAsync();
    Console.WriteLine("Connected to emulator transport");
}
catch (Exception ex)
{
    Console.WriteLine($"Emulator connect failed: {ex.Message}");
    Console.WriteLine("Trying real HID device...");

    // Try real WinRT transport (P/Invoke hid.dll)
    // Note: WinrtTransport uses P/Invoke, not WinRT API, so it works without SDK targeting packs
    Console.WriteLine("Real HID transport would be used here with actual hardware.");
    return;
}

// Test keymap binding
Console.WriteLine("\n--- Testing Keymap Binding ---");
try
{
    var changed = await KeymapManager.ApplyAsync(rpcDevice);
    Console.WriteLine($"Keymap applied: {changed}");
}
catch (Exception ex)
{
    Console.WriteLine($"Keymap apply failed: {ex.Message}");
}

// Test device status
Console.WriteLine("\n--- Testing Device Status ---");
try
{
    var response = await rpcDevice.RequestAsync("device.status", null);
    Console.WriteLine($"Response: {response.Result}");
}
catch (Exception ex)
{
    Console.WriteLine($"Status request failed: {ex.Message}");
}

// Test lighting
Console.WriteLine("\n--- Testing Lighting ---");
try
{
    var threads = new List<Oai.Thread>
    {
        new Oai.Thread(1) { Color = 0x00C853, Brightness = 1, Effect = Oai.Effect.Solid },
        new Oai.Thread(2) { Color = 0xFFA000, Brightness = 0.5, Effect = Oai.Effect.Solid },
    };

    await rpcDevice.RequestAsync("v.oai.thstatus", Oai.ThreadsParams(threads));
    Console.WriteLine("Lighting command sent successfully");

    // Read back keymap to verify
    var config = await KeymapManager.ReadAsync(rpcDevice);
    var applied = KeymapManager.IsAgentKeymapApplied(config);
    Console.WriteLine($"Keymap applied on device: {applied}");
}
catch (Exception ex)
{
    Console.WriteLine($"Lighting test failed: {ex.Message}");
}

await rpcDevice.DisposeAsync();
Console.WriteLine("\nDone!");
