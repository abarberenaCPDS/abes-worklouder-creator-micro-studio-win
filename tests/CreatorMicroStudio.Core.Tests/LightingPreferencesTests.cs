using System.Text.Json.Nodes;
using AbesWorkLouderCreatorMicroStudio.Windows.Core.Config;
using AbesWorkLouderCreatorMicroStudio.Windows.Core.Pad;

namespace AbesWorkLouderCreatorMicroStudio.Windows.Core.Tests;

public sealed class LightingPreferencesTests
{
    [Fact]
    public void DefaultsMatchTheStudioLightingPalette()
    {
        var preferences = LightingPreferences.Parse("{}");

        Assert.Equal(1, preferences.Brightness);
        Assert.Equal(0.5, preferences.Speed);
        Assert.Equal(0xFF2D2D, preferences.Status["blocked"].Color);
        Assert.Equal(Oai.Effect.Breath, preferences.Status["blocked"].Effect);
        Assert.Equal(0xFFA000, preferences.Status["working"].Color);
        Assert.Equal(0x00B0FF, preferences.Status["done"].Color);
        Assert.Equal(0x00C853, preferences.Status["idle"].Color);
        Assert.Equal(LightingPreferences.UnderglowMode.Status, preferences.Underglow.Mode);
        Assert.Equal(Oai.Effect.Rainbow, preferences.Underglow.Look.Effect);
        Assert.Equal(LightingPreferences.BacklightMode.Off, preferences.Backlight.Mode);
        Assert.Empty(preferences.Keys);
    }

    [Fact]
    public void ParsesEachFieldIndependentlyAndClampsUnitValues()
    {
        var preferences = LightingPreferences.Parse("""
            {
              "lighting": {
                "brightness": 2,
                "speed": "-0.25",
                "status": {
                  "blocked": { "c": "#ABC", "e": "shallow_breath", "b": 0.4 },
                  "working": { "c": "not-a-colour", "e": 99, "s": 2 }
                },
                "underglow": { "mode": "CUSTOM", "c": 1122867, "e": 6 },
                "backlight": { "mode": "bad-mode", "color": "#010203" },
                "keys": {
                  "0": { "c": "#123456", "e": "Breathing" },
                  "12": { "color": 16777215 },
                  "13": { "c": "#FFFFFF" },
                  "3": { "e": "solid" }
                }
              }
            }
            """);

        Assert.Equal(1, preferences.Brightness);
        Assert.Equal(0, preferences.Speed);
        Assert.Equal(0xAABBCC, preferences.Status["blocked"].Color);
        Assert.Equal(Oai.Effect.ShallowBreath, preferences.Status["blocked"].Effect);
        Assert.Equal(0.4, preferences.Status["blocked"].Brightness);
        Assert.Equal(0xFFA000, preferences.Status["working"].Color);
        Assert.Equal(Oai.Effect.Solid, preferences.Status["working"].Effect);
        Assert.Equal(1, preferences.Status["working"].Speed);
        Assert.Equal(LightingPreferences.UnderglowMode.Custom, preferences.Underglow.Mode);
        Assert.Equal(0x112233, preferences.Underglow.Look.Color);
        Assert.Equal(Oai.Effect.ShallowBreath, preferences.Underglow.Look.Effect);
        Assert.Equal(LightingPreferences.BacklightMode.Off, preferences.Backlight.Mode);
        Assert.Equal(0x010203, preferences.Backlight.Look.Color);
        Assert.Equal(0x123456, preferences.Keys[0].Color);
        Assert.Equal(Oai.Effect.Breath, preferences.Keys[0].Effect);
        Assert.Equal(0xFFFFFF, preferences.Keys[12].Color);
        Assert.DoesNotContain(13, preferences.Keys.Keys);
        Assert.DoesNotContain(3, preferences.Keys.Keys);
    }

    [Fact]
    public void SerializesCanonicalShapeAndRoundTripsLooks()
    {
        var preferences = LightingPreferences.Parse("""
            {
              "lighting": {
                "brightness": 0.7,
                "speed": 0.2,
                "status": { "idle": { "color": "#123456", "effect": "snake" } },
                "underglow": { "mode": "off", "c": "#112233", "e": "rainbow", "b": 0.6 },
                "backlight": { "mode": "custom", "c": "#445566" },
                "keys": { "7": { "c": "#AABBCC", "e": "gradient", "s": 0.9 } }
              }
            }
            """);

        var serialized = preferences.ToJsonObject();
        Assert.Equal("#123456", serialized["status"]?["idle"]?["c"]?.GetValue<string>());
        Assert.Equal("snake", serialized["status"]?["idle"]?["e"]?.GetValue<string>());
        Assert.Equal("#AABBCC", serialized["keys"]?["7"]?["c"]?.GetValue<string>());
        Assert.Equal("gradient", serialized["keys"]?["7"]?["e"]?.GetValue<string>());
        Assert.Equal(preferences.Status["idle"], LightingPreferences.Parse(serialized).Status["idle"]);
    }

    [Fact]
    public void SaveUpdateResetPreserveUnknownConfigSections()
    {
        WithStore(store =>
        {
            store.Update(json =>
            {
                json["future"] = new JsonObject { ["keep"] = true };
                json["actions"] = new JsonObject { ["0"] = "focus-agent" };
                json["lighting"] = new JsonObject { ["brightness"] = 0.25, ["foreign"] = "replaced-by-canonical-lighting" };
            });

            var loaded = LightingPreferences.Load(store);
            LightingPreferences.Update(store, current => current.Speed = 0.8);
            var updated = store.Read();

            Assert.True(updated["future"]?["keep"]?.GetValue<bool>());
            Assert.Equal("focus-agent", updated["actions"]?["0"]?.GetValue<string>());
            Assert.Equal(0.25, updated["lighting"]?["brightness"]?.GetValue<double>());
            Assert.Equal(0.8, updated["lighting"]?["speed"]?.GetValue<double>());
            Assert.Equal(0.25, loaded.Brightness);

            LightingPreferences.Reset(store);
            Assert.Null(store.Read()["lighting"]);
            Assert.True(store.Read()["future"]?["keep"]?.GetValue<bool>());
        });
    }

    [Fact]
    public void RebasedSaveKeepsConcurrentEditsAndAppliesOnlyDraftChanges()
    {
        WithStore(store =>
        {
            var baseline = LightingPreferences.Defaults;
            LightingPreferences.Save(baseline, store);
            var draft = LightingPreferences.Load(store);
            draft.Brightness = 0.4;
            draft.Keys[2] = new LightingPreferences.Look(0x123456);

            LightingPreferences.Update(store, current =>
            {
                current.Speed = 0.9;
                current.Status["idle"] = new LightingPreferences.Look(0x654321);
            });

            var saved = LightingPreferences.Save(draft, baseline, store);

            Assert.Equal(0.4, saved.Brightness);
            Assert.Equal(0.9, saved.Speed);
            Assert.Equal(0x654321, saved.Status["idle"].Color);
            Assert.Equal(0x123456, saved.Keys[2].Color);
        });
    }

    [Fact]
    public void LookCanCreateAHostSideThreadOrZoneWithoutTouchingKeymap()
    {
        var look = new LightingPreferences.Look(0xAABBCC, Oai.Effect.Breath, 0.5, 0.25);

        var thread = look.Thread(7, 0.8, 0.9);
        var zone = look.Zone(0.8, 0.9);

        Assert.Equal(7, thread.Id);
        Assert.Equal(0xAABBCC, thread.Color);
        Assert.Equal(0.4, thread.Brightness);
        Assert.Equal(0.25, thread.Speed);
        Assert.Equal(0.4, zone.Brightness);
        Assert.Equal(0.25, zone.Speed);
    }

    private static void WithStore(Action<StudioConfigStore> test)
    {
        var directory = Path.Combine(Path.GetTempPath(), "lighting-preferences-tests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(directory);
            test(new StudioConfigStore(Path.Combine(directory, "config.json")));
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch { }
        }
    }
}
