using Microsoft.Playwright;
using ClankYankers.Studio.AcceptanceTests.Support;
using Reqnroll;

namespace ClankYankers.Studio.AcceptanceTests.Bindings;

[Binding]
public sealed class StudioScreenshotSteps
{
    private readonly ScenarioContext _scenarioContext;

    public StudioScreenshotSteps(ScenarioContext scenarioContext)
    {
        _scenarioContext = scenarioContext;
    }

    [Given(@"I navigate to the overview page at (\d+)x(\d+) in (light|dark) mode")]
    public Task GivenINavigateToOverviewAsync(int width, int height, string theme) =>
        NavigateAndPrepareAsync("/#/overview", width, height, theme);

    [Given(@"I navigate to the sessions page at (\d+)x(\d+) in (light|dark) mode")]
    public Task GivenINavigateToSessionsAsync(int width, int height, string theme) =>
        NavigateAndPrepareAsync("/#/sessions", width, height, theme);

    [Then(@"a screenshot is saved as ""(.+)""")]
    public async Task ThenAScreenshotIsSavedAsAsync(string fileName)
    {
        var outputDir = ScreenshotDirectory();
        Directory.CreateDirectory(outputDir);
        var path = Path.Combine(outputDir, fileName);
        await Page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = path,
            FullPage = false,
        });
    }

    private async Task NavigateAndPrepareAsync(string route, int width, int height, string theme)
    {
        await Page.SetViewportSizeAsync(width, height);
        await Page.GotoAsync(route);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await Page.AddStyleTagAsync(new PageAddStyleTagOptions
        {
            Content = """
                *,
                *::before,
                *::after {
                  animation: none !important;
                  transition: none !important;
                }
                """,
        });
        await Page.EvaluateAsync(
            "(theme) => { if (theme === 'dark') document.documentElement.setAttribute('data-theme', 'dark'); else document.documentElement.removeAttribute('data-theme'); }",
            theme);
        // Brief settle so paints flush before screenshot
        await Task.Delay(120);
    }

    /// <summary>
    /// Resolves the output directory for screenshots.
    /// When the SCREENSHOT_OUTPUT_DIR environment variable is set (e.g. in CI), that path is used.
    /// Otherwise screenshots land in docs/assets/bdd relative to the repository root.
    /// </summary>
    private static string ScreenshotDirectory()
    {
        var envDir = Environment.GetEnvironmentVariable("SCREENSHOT_OUTPUT_DIR");
        if (!string.IsNullOrWhiteSpace(envDir))
        {
            return envDir;
        }

        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "ClankYankers.slnx")))
            {
                return Path.Combine(current.FullName, "docs", "assets", "bdd");
            }

            current = current.Parent;
        }

        return Path.Combine(AppContext.BaseDirectory, "screenshots");
    }

    private IPage Page =>
        _scenarioContext.TryGetValue(WorkspaceHooks.PageKey, out IPage? page) && page is not null
            ? page
            : throw new InvalidOperationException("Scenario page is not available.");
}
