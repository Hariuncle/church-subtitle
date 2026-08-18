namespace ChurchSubtitle.Core.Tests;

public sealed class WebUiAssetTests
{
    private static readonly string WebRoot = Path.Combine(FindRepositoryRoot(), "src", "ChurchSubtitle.Web", "wwwroot");

    [Fact]
    public void Index_ContainsAccessibleControlsAndCaptionRegions()
    {
        var html = ReadAsset("index.html");

        Assert.Contains("id=\"player\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"delay\"", html, StringComparison.Ordinal);
        Assert.Contains("<option value=\"low\" selected", html, StringComparison.Ordinal);
        Assert.Contains("<option value=\"medium\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"start-button\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"stop-button\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"status-badge\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"caption-text\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-live=\"polite\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-atomic=\"false\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"live-indicator\"", html, StringComparison.Ordinal);

        Assert.Equal(1, CountOccurrences(html, "id=\"caption-text\""));
        Assert.DoesNotContain("id=\"caption-line-1\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"caption-line-2\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"duration\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"current-caption\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"caption-history\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void App_StreamsFullPcmFromCurrentPlaybackPosition()
    {
        var script = ReadAsset("app.js");

        Assert.Contains("YLsWQq04R10", script, StringComparison.Ordinal);
        Assert.Contains("/ws/captions", script, StringComparison.Ordinal);
        Assert.Contains("/test-assets/service.pcm", script, StringComparison.Ordinal);
        Assert.Contains("export function pcmStartByteForVideoTime", script, StringComparison.Ordinal);
        Assert.Contains("player.getCurrentTime()", script, StringComparison.Ordinal);
        Assert.Contains("pcmStartByteForVideoTime", script, StringComparison.Ordinal);
        Assert.Contains("Range: `bytes=${startByte}-`", script, StringComparison.Ordinal);
        Assert.Contains("response.body.getReader()", script, StringComparison.Ordinal);
        Assert.Contains("reader.read()", script, StringComparison.Ordinal);
        Assert.DoesNotContain("arrayBuffer()", script, StringComparison.Ordinal);

        Assert.Contains("48000", script, StringComparison.Ordinal);
        Assert.Contains("4800", script, StringComparison.Ordinal);
        Assert.Contains("100", script, StringComparison.Ordinal);
        Assert.Contains("performance.now()", script, StringComparison.Ordinal);
        Assert.Contains("bufferedAmount", script, StringComparison.Ordinal);
        Assert.Contains("wss:", script, StringComparison.Ordinal);
        Assert.Contains("ws:", script, StringComparison.Ordinal);
        Assert.Contains("JSON.stringify({ type: \"start\", delay:", script, StringComparison.Ordinal);
        Assert.Contains("JSON.stringify({ type: \"end\" })", script, StringComparison.Ordinal);
        Assert.Contains("const CONNECTION_TIMEOUT_MS = 5000", script, StringComparison.Ordinal);
        Assert.Contains("const PLAYBACK_WAIT_TIMEOUT_MS = 8000", script, StringComparison.Ordinal);
        Assert.Contains("playerState === \"playing\"", script, StringComparison.Ordinal);

        Assert.DoesNotContain("VIDEO_START_SECONDS", script, StringComparison.Ordinal);
        Assert.DoesNotContain("VALID_DURATIONS", script, StringComparison.Ordinal);
        Assert.DoesNotContain("durationSeconds", script, StringComparison.Ordinal);
        Assert.DoesNotContain("565", script, StringComparison.Ordinal);
    }

    [Fact]
    public void App_ProjectsCaptionsIntoExactlyTwoStableTextRows()
    {
        var script = ReadAsset("app.js");

        Assert.Contains("captionText.textContent", script, StringComparison.Ordinal);
        Assert.Contains("renderCaption", script, StringComparison.Ordinal);
        Assert.Contains("MAX_UI_FINAL_FRAGMENTS", script, StringComparison.Ordinal);
        Assert.Contains("type=\"module\"", ReadAsset("index.html"), StringComparison.Ordinal);
        Assert.Contains("textContent", script, StringComparison.Ordinal);
        Assert.DoesNotContain("innerHTML", script, StringComparison.Ordinal);
        Assert.DoesNotContain("finalNodes", script, StringComparison.Ordinal);
        Assert.DoesNotContain("captionHistory", script, StringComparison.Ordinal);

        var browserAssets = script + ReadAsset("index.html") + ReadAsset("styles.css");
        Assert.DoesNotContain("OPENAI_API_KEY", browserAssets, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("apiKey", browserAssets, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Styles_IncludeResponsiveLayoutAndVideoAspectRatio()
    {
        var styles = ReadAsset("styles.css");

        Assert.Contains("aspect-ratio: 16 / 9", styles, StringComparison.Ordinal);
        Assert.Contains("@media", styles, StringComparison.Ordinal);
        Assert.Contains("max-width", styles, StringComparison.Ordinal);
        Assert.Contains(":focus-visible", styles, StringComparison.Ordinal);
        Assert.Contains("overflow-wrap", styles, StringComparison.Ordinal);
    }

    [Fact]
    public void Styles_KeepBothCurrentAndCachedMarkupInsideTwoVisualLines()
    {
        var styles = ReadAsset("styles.css");

        Assert.Matches(
            @"(?s)\.caption-lines\s*\{[^}]*block-size:\s*2\.9em;[^}]*overflow:\s*hidden;",
            styles);
        Assert.Matches(
            @"(?s)\.caption-line\s*\{[^}]*display:\s*inline;[^}]*margin:\s*0;",
            styles);
        Assert.Contains(".caption-line + .caption-line::before", styles, StringComparison.Ordinal);
        Assert.Contains("content: \" \";", styles, StringComparison.Ordinal);
    }

    [Fact]
    public void Program_DisablesBrowserCachingForLocalStaticAssets()
    {
        var program = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src",
            "ChurchSubtitle.Web",
            "Program.cs"));

        Assert.Contains("UseStaticFiles(new StaticFileOptions", program, StringComparison.Ordinal);
        Assert.Contains("no-store, no-cache, must-revalidate", program, StringComparison.Ordinal);
        Assert.Contains("Headers.Pragma = \"no-cache\"", program, StringComparison.Ordinal);
        Assert.Contains("Headers.Expires = \"0\"", program, StringComparison.Ordinal);
    }

    private static int CountOccurrences(string text, string value) =>
        text.Split(value, StringSplitOptions.None).Length - 1;

    private static string ReadAsset(string fileName) =>
        File.ReadAllText(Path.Combine(WebRoot, fileName));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ChurchSubtitle.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}
