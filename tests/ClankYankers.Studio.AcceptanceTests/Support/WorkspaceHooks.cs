using Microsoft.Playwright;
using Reqnroll;

namespace ClankYankers.Studio.AcceptanceTests.Support;

[Binding]
public sealed class WorkspaceHooks
{
    internal const string BrowserContextKey = "browser-context";
    internal const string PageKey = "page";

    private static IPlaywright? _playwright;
    private static IBrowser? _browser;
    private readonly ScenarioContext _scenarioContext;

    public WorkspaceHooks(ScenarioContext scenarioContext)
    {
        _scenarioContext = scenarioContext;
    }

    [BeforeTestRun]
    public static async Task BeforeTestRunAsync()
    {
        await StudioAcceptanceHost.Instance.EnsureStartedAsync();
        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
        });
    }

    [AfterTestRun]
    public static async Task AfterTestRunAsync()
    {
        if (_browser is not null)
        {
            await _browser.CloseAsync();
            _browser = null;
        }

        _playwright?.Dispose();
        _playwright = null;
        await StudioAcceptanceHost.Instance.DisposeAsync();
    }

    [BeforeScenario]
    public async Task BeforeScenarioAsync()
    {
        if (_browser is null)
        {
            throw new InvalidOperationException("The Playwright browser has not been initialized.");
        }

        var context = await _browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = StudioAcceptanceHost.BaseUrl,
        });
        var page = await context.NewPageAsync();
        _scenarioContext[BrowserContextKey] = context;
        _scenarioContext[PageKey] = page;
    }

    [AfterScenario]
    public async Task AfterScenarioAsync()
    {
        if (_scenarioContext.TryGetValue(PageKey, out IPage? page) && page is not null && _scenarioContext.TestError is not null)
        {
            var artifactDirectory = Path.Combine(AppContext.BaseDirectory, "artifacts");
            Directory.CreateDirectory(artifactDirectory);
            var screenshotPath = Path.Combine(artifactDirectory, $"{Sanitize(_scenarioContext.ScenarioInfo.Title)}.png");
            await page.ScreenshotAsync(new PageScreenshotOptions
            {
                FullPage = true,
                Path = screenshotPath,
            });
        }

        if (_scenarioContext.TryGetValue(BrowserContextKey, out IBrowserContext? context) && context is not null)
        {
            await context.CloseAsync();
        }
    }

    private static string Sanitize(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        return string.Join(string.Empty, value.Select(ch => invalidChars.Contains(ch) ? '-' : ch));
    }
}
