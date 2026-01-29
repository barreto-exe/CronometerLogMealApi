# Cronometer Log Meal API & Telegram Bot

A .NET 8 Web API and Background Service that acts as a bridge between **Telegram** and **Cronometer**. It allows users to log meals into Cronometer by simply sending natural language messages to a Telegram bot. The system uses AI (LLMs) to parse the text into structured nutritional data.

## Features

- **Telegram Integration**:
  - **Login**: Authenticate with Cronometer directly from Telegram using `/login <email> <password>`.
  - **Natural Language Logging**: Send messages like "Breakfast: 2 eggs and a slice of toast" or "Almuerzo: 200g de carne y ensalada".
  - **Feedback**: The bot replies with the status of the operation.

- **AI-Powered Parsing**:
  - Uses an AI client (configured via OpenAI-compatible options) to interpret meal descriptions.
  - Extracts meal category (Breakfast, Lunch, Dinner, Snack), date/time, and food items with quantities and units.

- **Cronometer Client**:
  - Reverse-engineered typed HttpClient for Cronometer Mobile API v2.
  - **Smart Food Search**: Searches foods in a specific order (Custom → Favorites → Common → Supplements → All).
  - **Unit Matching**: Intelligently matches user-provided units (e.g., "cup", "g", "oz") to Cronometer's available measures.
  - **Multi-Add**: Logs multiple items in a single request.

- **Web API**:
  - Swagger/OpenAPI support for testing endpoints.
  - Endpoints for manual logging and debugging.

## Tech Stack

- **Framework**: .NET 8 (ASP.NET Core Web API + Worker Service)
- **Architecture**:
  - `CronometerPollingHostedService`: Background service that polls Telegram updates.
  - `CronometerService`: Core logic for interacting with Cronometer.
  - `TelegramService`: Handles Telegram API interactions.
  - `OpenAIClient` / `GeminiClient`: Handles AI text generation.
- **Libraries**:
  - `F23.StringSimilarity`: For fuzzy matching of unit names.
  - `Swashbuckle.AspNetCore`: For Swagger documentation.

## Configuration

Create an `appsettings.json` file in the root of the project (or use User Secrets) with the following structure.

> **Note**: The project currently uses an `OpenAIHttpClient` in the hosted service, so you must configure the `OpenAI` section. It also supports **Azure Vision** for OCR capabilities and **Gemini** for extended AI features (including those requiring cookies).

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "Telegram": {
    "BotToken": "YOUR_TELEGRAM_BOT_TOKEN"
  },
  "Gemini": {
    "ApiKey": "YOUR_GEMINI_API_KEY",
    "Model": "gemini-2.0-flash",
    "VisionModel": "gemini-2.0-flash",
    "BaseUrl": "https://generativelanguage.googleapis.com/v1beta",
    "Cookies": {
      "__Secure-1PSID": "YOUR_SECURE_1PSID",
      "__Secure-1PSIDTS": "YOUR_SECURE_1PSIDTS"
    }
  },
  "OpenAI": {
    "ApiKey": "YOUR_OPENAI_API_KEY",
    "Model": "gpt-4o-mini"
  },
  "AzureVision": {
    "Endpoint": "YOUR_AZURE_VISION_ENDPOINT",
    "ApiKey": "YOUR_AZURE_VISION_API_KEY"
  }
}
```

*   **Telegram**: Get your bot token from @BotFather.
*   **OpenAI**: Used by the main bot logic to parse meals.
*   **Gemini**: Used for specific AI tasks. Note that some models/features might require browser cookies (`__Secure-1PSID` and `__Secure-1PSIDTS`) to bypass certain restrictions or access specific capabilities.
*   **AzureVision**: (Optional) Used for OCR (Optical Character Recognition) to extract text from images sent to the bot.

## Running the Project

1.  **Prerequisites**:
    *   .NET 8 SDK installed.
    *   A Cronometer account.
    *   A Telegram Bot Token.
    *   An API Key for OpenAI or Gemini.

2.  **Start the Application**:
    ```bash
    cd CronometerLogMealApi
    dotnet run
    ```

3.  **Using the Bot**:
    *   Open your bot in Telegram.
    *   **Login**: Send `/login user@email.com mypassword`.
    *   **Log a Meal**: Send a message describing your food.
        *   *Example*: "Desayuno: 2 huevos revueltos y 1 pan tostado"
        *   *Example*: "Lunch: 150g chicken breast and 1 cup of rice"

## API Endpoints

If running locally, visit `http://localhost:5000/swagger` (or the port configured in `launchSettings.json`) to see the Swagger UI.

*   `POST /api/Cronometer/Log-meal`: Manual logging endpoint.
*   `GET /api/Telegram/updates`: Manually fetch Telegram updates.
*   `POST /api/Gemini/ask`: Test the Gemini integration directly.

## Project Structure

*   `Clients/`: HTTP Clients for external services (Cronometer, Telegram, OpenAI, Gemini).
*   `Controllers/`: API Controllers.
*   `Services/`: Business logic and background services.
    *   `CronometerPollingHostedService.cs`: The heart of the bot, polling for messages and coordinating the flow.
*   `Models/`: Data models and DTOs.

