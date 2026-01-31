# Cronometer Log Meal API - Copilot Instructions

## Project Overview
A .NET 8 Web API + Background Service that bridges Telegram and Cronometer. Users send natural language meal descriptions via Telegram, which are parsed by AI (OpenAI/Gemini) and logged to Cronometer's unofficial mobile API.

## Architecture

### Core Flow
1. **Telegram → Polling Service** → receives user messages
2. **LLM Processing** → parses natural language to `LogMealRequest` JSON
3. **Cronometer Validation** → searches foods, matches units, validates items
4. **Conversation State Machine** → handles clarifications, confirmations
5. **Cronometer API** → logs validated meals via `multi_add_serving`

### Key Components

| Component | Location | Purpose |
|-----------|----------|---------|
| `CronometerPollingHostedService` | [Services/](CronometerLogMealApi/Services/CronometerPollingHostedService.cs) | Main entry point, polls Telegram, orchestrates flow |
| `ICommandHandler` implementations | [Handlers/Commands/](CronometerLogMealApi/Handlers/Commands/) | `/login`, `/start`, `/save`, `/cancel`, `/preferences`, `/search` |
| `IStateProcessor` implementations | [Handlers/StateProcessors/](CronometerLogMealApi/Handlers/StateProcessors/) | Handle each `ConversationState` |
| `CronometerService` | [Services/CronometerService.cs](CronometerLogMealApi/Services/CronometerService.cs) | Food search (Custom→Favorites→Common→Supplements→All), unit matching |
| `LlmMealProcessor` | [Services/LlmMealProcessor.cs](CronometerLogMealApi/Services/LlmMealProcessor.cs) | Parses meal text via OpenAI/Gemini |
| `MealValidationOrchestrator` | [Services/MealValidationOrchestrator.cs](CronometerLogMealApi/Services/MealValidationOrchestrator.cs) | Validates items against Cronometer DB |

### Conversation State Machine
States are defined in `ConversationState` enum ([ConversationSession.cs](CronometerLogMealApi/Models/ConversationSession.cs#L119)):
- `Idle` → `AwaitingMealDescription` → `Processing` → `AwaitingClarification`/`AwaitingConfirmation` → back to `Idle`
- Each state has a dedicated `IStateProcessor` in `Handlers/StateProcessors/`

### External API Clients
All HTTP clients are in `Clients/` with typed request/response models:
- **CronometerHttpClient**: Reverse-engineered mobile API (base: `https://mobile.cronometer.com/api/v2/`)
- **TelegramHttpClient**: Bot API wrapper
- **OpenAIHttpClient**: OpenAI-compatible (works with GitHub Models, Azure)
- **GeminiHttpClient**: Google Gemini API
- **AzureVisionService**: OCR for photo messages

## Conventions

### Adding New Commands
1. Create handler in `Handlers/Commands/` implementing `ICommandHandler`
2. Register in `Program.cs`: `builder.Services.AddTransient<ICommandHandler, YourCommandHandler>()`
3. Implement `CanHandle(string? command)` to match the command text

### Adding New Conversation States
1. Add state to `ConversationState` enum in `Models/ConversationSession.cs`
2. Create processor in `Handlers/StateProcessors/` implementing `IStateProcessor`
3. Set `HandledState` property to match your new state
4. Register in `Program.cs` as both concrete and `IStateProcessor`

### Telegram Messages
All user-facing messages are in [TelegramMessages.cs](CronometerLogMealApi/Constants/TelegramMessages.cs) - organized by category (`Auth`, `Session`, `Meal`, `Search`, `Memory`). Always use these constants; messages are in Spanish.

### LLM Prompts
Prompts live in [GeminiPrompts.cs](CronometerLogMealApi/Clients/GeminiClient/GeminiPrompts.cs). The main `CronometerPrompt` uses placeholders `@Now` and `@UserInput`. Output is `LogMealRequestWithClarifications` JSON.

### Memory/Alias System
Firebase Firestore stores user food aliases (`UserMemoryService`). Aliases are detected pre-LLM processing and replaced with actual food names. See `Models/UserMemory/` for data structures.

## Development

### Running Locally
```bash
cd CronometerLogMealApi
dotnet run
```
Access Swagger at `http://localhost:5000/swagger` (port varies by `launchSettings.json`)

### Configuration
Copy `appsettings.Example.json` → `appsettings.json` and fill in:
- `Telegram:BotToken` - from @BotFather
- `OpenAI:ApiKey` + `BaseUrl` - supports OpenAI, GitHub Models, Azure OpenAI
- `Firebase:ProjectId` - optional, enables memory features

### Key Patterns
- **Optional services**: Services like `UserMemoryService` are injected as nullable and checked at runtime
- **Auth flow**: `CronometerUserInfo` holds `SessionKey` + `UserId` after `/login`
- **Unit matching**: Uses `F23.StringSimilarity` for fuzzy matching user units to Cronometer measures
- **Search priority**: Custom foods → Favorites → Common → Supplements → All (see `SearchFoodWithCandidatesAsync`)
