namespace AbesWorkLouderCreatorMicroStudio.Windows.Core.Tests;

using AbesWorkLouderCreatorMicroStudio.Windows.Core.Pad;

public class LightingEditorTests
{
    [Fact]
    public void TogglingAKeySelectsAndDeselectsIt()
    {
        var editor = new LightingEditor();

        editor.Toggle(6);
        Assert.True(editor.IsSelected(6));

        editor.Toggle(6);
        Assert.False(editor.IsSelected(6));
        Assert.Empty(editor.Selection);
    }

    /// The stock layout's agent keys, in reading order. A configured layout is
    /// passed in by the Lighting page; this pins the no-argument default.
    [Fact]
    public void TheAgentKeysAreTheStockLayout()
    {
        var editor = new LightingEditor();
        editor.SelectAgentKeys();

        Assert.Equal(Pad.AgentKeyIds.Order(), editor.Selection.Order());
    }

    [Fact]
    public void EveryKeyIsTheWholePad()
    {
        var editor = new LightingEditor();
        editor.SelectEveryKey();

        Assert.Equal(Pad.KeyCount, editor.Selection.Count);
    }

    [Fact]
    public void SelectingNoneForgetsTheSelection()
    {
        var editor = new LightingEditor();
        editor.SelectEveryKey();
        editor.SelectNone();

        Assert.Empty(editor.Selection);
    }

    [Fact]
    public void TheEditReachesEverySelectedKey()
    {
        var editor = new LightingEditor
        {
            Color = 0x112233,
            Effect = Oai.Effect.Breath,
            Brightness = 0.25,
            Speed = 0.75,
        };
        editor.Toggle(4);
        editor.Toggle(9);

        var threads = editor.SelectedThreads();

        Assert.Equal(new[] { 4, 9 }, threads.Select(t => t.Id));
        Assert.All(threads, t =>
        {
            Assert.Equal(0x112233, t.Color);
            Assert.Equal(Oai.Effect.Breath, t.Effect);
            Assert.Equal(0.25, t.Brightness);
            Assert.Equal(0.75, t.Speed);
        });
    }

    /// Only the selection is sent, so the keys either side of it keep whatever
    /// the bridge, or an earlier apply, put there.
    [Fact]
    public void UnselectedKeysAreNotSentAtAll()
    {
        var editor = new LightingEditor();
        editor.Toggle(2);

        Assert.Single(editor.SelectedThreads());
    }

    [Fact]
    public void TheUnderglowCarriesItsOwnEdit()
    {
        var editor = new LightingEditor
        {
            AmbientColor = 0x00FF00,
            AmbientEffect = Oai.Effect.Rainbow,
            AmbientBrightness = 0.5,
            AmbientSpeed = 0.1,
        };

        var zone = editor.AmbientZone();

        Assert.Equal(0x00FF00, zone.Color);
        Assert.Equal(Oai.Effect.Rainbow, zone.Effect);
        Assert.Equal(0.5, zone.Brightness);
        Assert.Equal(0.1, zone.Speed);
    }

    [Fact]
    public void TheKeysZoneCarriesTheKeyEdit()
    {
        var editor = new LightingEditor { Color = 0xFF0000, Effect = Oai.Effect.Snake };
        editor.SelectAgentKeys();

        var zone = editor.KeysZone();

        Assert.Equal(0xFF0000, zone.Color);
        Assert.Equal(Oai.Effect.Snake, zone.Effect);
    }

    /// Ids run to AG19, past the thirteen physical keys: the dial and joystick
    /// answer to those codes, and a stale light there would outlive a reset.
    [Fact]
    public void AllOffCoversEveryThreadIdAndIsDark()
    {
        var threads = LightingEditor.AllOffThreads();

        Assert.Equal(Pad.MaxThreadId + 1, threads.Count);
        Assert.Equal(Enumerable.Range(0, Pad.MaxThreadId + 1), threads.Select(t => t.Id));
        Assert.All(threads, t =>
        {
            Assert.Equal(0d, t.Brightness);
            Assert.Equal(Oai.Effect.Off, t.Effect);
            Assert.False(t.SyncKeys);
            Assert.False(t.SyncAmbient);
        });
    }

    [Fact]
    public void ClearingKeysTouchesOnlyThoseKeys()
    {
        var threads = LightingEditor.OffThreads(new[] { 5, 1 });

        Assert.Equal(new[] { 1, 5 }, threads.Select(t => t.Id));
        Assert.All(threads, t =>
        {
            Assert.Equal(0d, t.Brightness);
            Assert.Equal(Oai.Effect.Off, t.Effect);
        });
    }

    /// The two traps that fail silently on the wire: the abbreviations, and
    /// `effect` being an integer rather than an effect name.
    [Fact]
    public void ThePayloadKeepsTheFirmwaresAbbreviations()
    {
        var editor = new LightingEditor { Color = 0x00B0FF, Effect = Oai.Effect.ShallowBreath };
        editor.Toggle(3);

        var thread = editor.SelectedThreads()[0].Wire();

        Assert.Equal(new[] { "b", "c", "e", "id", "s" }, thread.Keys.Order());
        Assert.Equal(0x00B0FF, thread["c"]);
        Assert.Equal((int)Oai.Effect.ShallowBreath, thread["e"]);
        Assert.IsType<int>(thread["e"]);

        var zone = editor.AmbientZone().Wire();
        Assert.Equal(new[] { "b", "c", "e", "m", "s" }, zone.Keys.Order());
    }
}
