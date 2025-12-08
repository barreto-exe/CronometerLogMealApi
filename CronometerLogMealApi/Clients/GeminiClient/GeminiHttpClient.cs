using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;

namespace CronometerLogMealApi.Clients.GeminiClient;

public class GeminiHttpClient
{
    private readonly GeminiClientOptions _options;

    public GeminiHttpClient(IOptions<GeminiClientOptions> opts)
    {
        _options = opts.Value;
    }

    public async Task<GenerateContentResponse?> GenerateTextAsync(string prompt, CancellationToken ct = default)
    {
        return await ScrapeGeminiAsync(prompt);
    }

    public async Task<GenerateContentResponse?> GenerateContentAsync(GenerateContentRequest request, CancellationToken ct = default)
    {
        var prompt = request.Contents?.LastOrDefault()?.Parts?.LastOrDefault()?.Text;
        if (string.IsNullOrWhiteSpace(prompt)) return null;

        return await ScrapeGeminiAsync(prompt);
    }

    private async Task<GenerateContentResponse> ScrapeGeminiAsync(string prompt)
    {
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });

        var contextOptions = new BrowserNewContextOptions
        {
            UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"
        };

        await using var context = await browser.NewContextAsync(contextOptions);

        if (_options.Cookies != null && _options.Cookies.Count > 0)
        {
            var cookies = _options.Cookies.Select(c => new Cookie
            {
                Name = c.Key,
                Value = c.Value,
                Domain = ".google.com",
                Path = "/",
                Secure = true,
                SameSite = SameSiteAttribute.None
            });
            await context.AddCookiesAsync(cookies);
        }

        var page = await context.NewPageAsync();
        
        // Navigate to Gemini
        await page.GotoAsync("https://gemini.google.com/app");

        // Selectors
        var inputSelector = "div[contenteditable='true'][role='textbox']";
        var sendButtonSelector = "button[aria-label='Send message']";
        var responseSelector = ".model-response-text"; 

        try
        {
            await page.WaitForSelectorAsync(inputSelector, new PageWaitForSelectorOptions { Timeout = 30000 });
        }
        catch (TimeoutException)
        {
            if (page.Url.Contains("accounts.google.com"))
            {
                throw new Exception("Gemini Scraper: Redirected to login. Cookies might be invalid or expired.");
            }
            throw;
        }

        // Handle potential splash screen
        var closeButton = page.Locator("button[aria-label='Close']");
        if (await closeButton.IsVisibleAsync())
        {
            await closeButton.ClickAsync();
        }

        // Type prompt
        await page.ClickAsync(inputSelector);
        await page.FillAsync(inputSelector, prompt);

        // Click send
        await page.Locator(sendButtonSelector).ClickAsync();

        // Wait for response
        await page.WaitForTimeoutAsync(2000);
        await page.Locator(sendButtonSelector).WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 60000 });

        // Extract response
        var responses = page.Locator(responseSelector);
        var count = await responses.CountAsync();
        
        string text = "";
        if (count > 0)
        {
            text = await responses.Nth(count - 1).InnerTextAsync();
        }
        else
        {
            text = "Error: No response text found.";
        }

        return new GenerateContentResponse
        {
            Candidates = 
            [
                new GeminiCandidate
                {
                    Content = new GeminiContent
                    {
                        Role = "model",
                        Parts = [ new GeminiPart { Text = text } ]
                    },
                    FinishReason = "STOP"
                }
            ]
        };
    }
}
