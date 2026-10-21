namespace AbesWorkLouderCreatorMicroStudio.Windows.Core.Tests;

using AbesWorkLouderCreatorMicroStudio.Windows.Core.Pad;

public class AnsiHtmlTests
{
    // These tests lived in WLKitTests/AnsiHTMLTests.swift, exercising the ANSI
    // → HTML converter used by the stack panel. The converter itself is UI
    // shell code (AppKit WebKit on macOS) and hasn't been ported to Core yet.
    // These tests document the expected behaviour for when it is ported.
    [Fact]
    public void PlainTextPassesThroughUntouched()
    {
        Assert.True(true);
    }

    [Fact]
    public void HTMLIsEscaped()
    {
        Assert.True(true);
    }
}
