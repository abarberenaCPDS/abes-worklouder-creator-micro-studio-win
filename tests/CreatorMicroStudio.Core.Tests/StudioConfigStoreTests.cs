using System.Text.Json.Nodes;
using AbesWorkLouderCreatorMicroStudio.Windows.Core.Config;

namespace AbesWorkLouderCreatorMicroStudio.Windows.Core.Tests;

public sealed class StudioConfigStoreTests
{
    [Fact]
    public void UpdatePreservesUnknownSectionsAndCreatesConfig()
    {
        var directory = Path.Combine(Path.GetTempPath(), "micro-manager-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "config.json");
        try
        {
            var store = new StudioConfigStore(path);
            store.Update(json =>
            {
                json["future"] = new JsonObject { ["kept"] = true };
                json["actions"] = new JsonObject { ["0"] = "focus-agent" };
            });
            store.Update(json => json["lighting"] = new JsonObject { ["brightness"] = 0.5 });

            var saved = store.Read();
            Assert.True(saved["future"]?["kept"]?.GetValue<bool>());
            Assert.Equal("focus-agent", saved["actions"]?["0"]?.GetValue<string>());
            Assert.Equal(0.5, saved["lighting"]?["brightness"]?.GetValue<double>());
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch { }
        }
    }

    [Fact]
    public void RefusesToReplaceMalformedOrNonObjectConfig()
    {
        var directory = Path.Combine(Path.GetTempPath(), "micro-manager-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "config.json");
        try
        {
            File.WriteAllText(path, "[]");
            var store = new StudioConfigStore(path);
            Assert.Throws<InvalidDataException>(() => store.Update(json => json["actions"] = new JsonObject()));
            Assert.Equal("[]", File.ReadAllText(path));
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch { }
        }
    }

    [Fact]
    public void SetActionPersistsCanonicalOverrideAndKeepsOtherConfig()
    {
        var directory = Path.Combine(Path.GetTempPath(), "micro-manager-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "config.json");
        try
        {
            var store = new StudioConfigStore(path);
            store.Update(json =>
            {
                json["keys"] = new JsonObject { ["9"] = "legacy" };
                json["custom"] = "preserve";
            });
            KeyBindings.SetAction(9, new InputAction(InputActionKind.SubmitPrompt, "Run tests"), store);

            var parsed = KeyBindings.Parse(File.ReadAllBytes(path));
            Assert.Equal(new InputAction(InputActionKind.SubmitPrompt, "Run tests"), parsed.ActionFor(9));
            Assert.Equal("legacy", parsed.TextFor(9));
            Assert.Equal("preserve", store.Read()["custom"]?.GetValue<string>());
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch { }
        }
    }

    [Fact]
    public void ReadsLegacyPathAndMigratesOnFirstWrite()
    {
        var directory = Path.Combine(Path.GetTempPath(), "micro-manager-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var legacyPath = Path.Combine(directory, "micromanager", "config.json");
        var newPath = Path.Combine(directory, "creator-micro-studio", "config.json");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(legacyPath)!);
            File.WriteAllText(legacyPath, "{\"keys\":{\"9\":\"legacy macro\"},\"future\":true}");
            var store = new StudioConfigStore(newPath, legacyPath);

            Assert.Equal("legacy macro", KeyBindings.Parse(System.Text.Encoding.UTF8.GetBytes(store.Read().ToJsonString())).TextFor(9));
            store.Update(json => json["lighting"] = new JsonObject { ["brightness"] = 0.5 });

            Assert.True(File.Exists(newPath));
            Assert.True(File.Exists(legacyPath)); // source remains intact
            var migrated = store.Read();
            Assert.True(migrated["future"]?.GetValue<bool>());
            Assert.Equal("legacy macro", migrated["keys"]?["9"]?.GetValue<string>());
            Assert.Equal(0.5, migrated["lighting"]?["brightness"]?.GetValue<double>());
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch { }
        }
    }
}
