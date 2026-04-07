using Microsoft.Playwright;
using ClankYankers.Studio.AcceptanceTests.Support;
using Reqnroll;
using Xunit;

namespace ClankYankers.Studio.AcceptanceTests.Bindings;

[Binding]
public sealed class WorkspaceResponsivenessSteps
{
    private static readonly string[] FocusedWorkspaceControlIds =
    [
        "workspace-studio-nav-toggle",
        "refresh-sessions",
        "workspace-launch-blade-toggle",
        "single-pane",
    ];

    private static readonly string[] LaunchBladeControlIds =
    [
        "launch-backplane",
        "launch-host",
        "launch-connector",
        "launch-working-directory",
        "launch-cols",
        "launch-rows",
        "launch-session",
        "launch-model",
        "launch-permission-mode",
        "launch-agent",
        "launch-skip-permissions",
        "launch-allowed-tools",
    ];

    private readonly ScenarioContext _scenarioContext;

    public WorkspaceResponsivenessSteps(ScenarioContext scenarioContext)
    {
        _scenarioContext = scenarioContext;
    }

    [Given(@"I open the workspace at (\d+)x(\d+)")]
    public async Task GivenIOpenTheWorkspaceAt(int width, int height)
    {
        await Page.SetViewportSizeAsync(width, height);
        await Page.GotoAsync("/#/workspace");
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
        await Expect(Page.GetByTestId("product-page-workspace")).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Terminal work stays first-class" })).ToBeVisibleAsync();
    }

    [Then(@"the browser viewport stays locked without page overflow")]
    public async Task ThenTheBrowserViewportStaysLockedWithoutPageOverflow()
    {
        var metrics = await Page.EvaluateAsync<ViewportMetrics>(
            @"() => ({
              innerHeight: window.innerHeight,
              innerWidth: window.innerWidth,
              scrollHeight: document.documentElement.scrollHeight,
              scrollWidth: document.documentElement.scrollWidth,
              bodyScrollHeight: document.body.scrollHeight,
              bodyScrollWidth: document.body.scrollWidth
            })");

        Assert.True(metrics.ScrollWidth <= metrics.InnerWidth + 1, "The document width should stay inside the viewport.");
        Assert.True(metrics.BodyScrollWidth <= metrics.InnerWidth + 1, "The body width should stay inside the viewport.");
        Assert.True(metrics.ScrollHeight <= metrics.InnerHeight + 1, "The document height should stay locked to the viewport.");
        Assert.True(metrics.BodyScrollHeight <= metrics.InnerHeight + 1, "The body height should stay locked to the viewport.");
    }

    [Then(@"the focused workspace actions stay visible and interactable")]
    public async Task ThenTheFocusedWorkspaceActionsStayVisibleAndInteractable()
    {
        await Expect(Page.GetByTestId("studio-sidebar")).ToHaveAttributeAsync("aria-hidden", "true");
        foreach (var testId in FocusedWorkspaceControlIds)
        {
            await AssertVisibleAndInteractableAsync(Page.GetByTestId(testId));
        }
    }

    [When(@"I open the studio navigation overlay")]
    public async Task WhenIOpenTheStudioNavigationOverlay()
    {
        await Page.GetByTestId("workspace-studio-nav-toggle").ClickAsync();
        await Expect(Page.GetByTestId("studio-sidebar")).ToHaveAttributeAsync("aria-hidden", "false");
    }

    [Then(@"the studio navigation remains fully visible")]
    public async Task ThenTheStudioNavigationRemainsFullyVisible()
    {
        await AssertVisibleAndInteractableAsync(Page.GetByTestId("nav-section-overview"));
        await AssertVisibleAndInteractableAsync(Page.GetByTestId("nav-section-sessions"));
        await AssertVisibleAndInteractableAsync(Page.GetByTestId("nav-section-connectors"));
    }

    [When(@"I open the new session blade")]
    public async Task WhenIOpenTheNewSessionBlade()
    {
        await Page.GetByTestId("workspace-launch-blade-toggle").ClickAsync();
        await Expect(Page.GetByTestId("workspace-launch-blade")).ToHaveAttributeAsync("aria-hidden", "false");
    }

    [When(@"I switch the launch connector to Claude")]
    public async Task WhenISwitchTheLaunchConnectorToClaude()
    {
        await Page.GetByTestId("launch-connector").SelectOptionAsync("claude");
    }

    [Then(@"the launch blade fields stay reachable and visible")]
    public async Task ThenTheLaunchBladeFieldsStayReachableAndVisible()
    {
        foreach (var testId in LaunchBladeControlIds)
        {
            await AssertVisibleAndInteractableAsync(Page.GetByTestId(testId));
        }
    }

    private IPage Page =>
        _scenarioContext.TryGetValue(WorkspaceHooks.PageKey, out IPage? page) && page is not null
            ? page
            : throw new InvalidOperationException("Scenario page is not available.");

    private static ILocatorAssertions Expect(ILocator locator) => Microsoft.Playwright.Assertions.Expect(locator);

    private async Task AssertVisibleAndInteractableAsync(ILocator locator)
    {
        await locator.EvaluateAsync("element => element.scrollIntoView({ block: 'center', inline: 'nearest' })");
        await Expect(locator).ToBeVisibleAsync();

        var boundingBox = await locator.BoundingBoxAsync();
        Assert.NotNull(boundingBox);

        var viewport = await Page.EvaluateAsync<ViewportSize>(
            @"() => ({
              width: window.innerWidth,
              height: window.innerHeight
            })");

        Assert.True(boundingBox!.X >= 0, "The element should not overflow the viewport on the left.");
        Assert.True(boundingBox.Y >= 0, "The element should not overflow above the viewport.");
        Assert.True(boundingBox.X + boundingBox.Width <= viewport.Width + 1, "The element should fit within the viewport width.");
        Assert.True(boundingBox.Y + boundingBox.Height <= viewport.Height + 1, "The element should fit within the viewport height.");

        var isHitTarget = await locator.EvaluateAsync<bool>(
            @"element => {
              const rect = element.getBoundingClientRect();
              const centerX = rect.left + rect.width / 2;
              const centerY = rect.top + rect.height / 2;
              const target = document.elementFromPoint(centerX, centerY);
              return target === element || element.contains(target);
            }");

        Assert.True(isHitTarget, "The element should remain clickable at its visual center.");
    }

    private sealed class ViewportMetrics
    {
        public int InnerHeight { get; set; }
        public int InnerWidth { get; set; }
        public int ScrollHeight { get; set; }
        public int ScrollWidth { get; set; }
        public int BodyScrollHeight { get; set; }
        public int BodyScrollWidth { get; set; }
    }

    private sealed class ViewportSize
    {
        public int Width { get; set; }
        public int Height { get; set; }
    }
}
