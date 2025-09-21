# Cronometer Log Meal API

A minimal .NET 8 Web API that logs meals to the Cronometer Mobile API v2. It exposes a single endpoint that translates a simple meal payload into Cronometer servings and posts them in one shot.

## Tech stack
- ASP.NET Core 8 Web API
- Typed HttpClient for Cronometer Mobile API v2
- Swagger/OpenAPI for interactive docs (in Development)

## Endpoint
- POST `/api/Cronometer/Log-meal`
  - Headers:
    - `X-User-Id`: long (Cronometer user ID)
    - `X-Auth-Token`: string (Cronometer auth token)
  - Body:
    - `category`: string — breakfast | lunch | dinner | snacks (used to set Cronometer order)
    - `date`: string/date — ISO date (e.g., 2025-09-19). Defaults to now if omitted.
    - `logTime`: boolean (optional) - If true, logs the time part of the `date`. Defaults to false.
    - `items`: array of meal items
      - `quantity`: number
      - `unit`: string (e.g., g, grams, ml, tbsp)
      - `name`: string (food name to search in Cronometer)

### Behavior
- Resolves each item by searching Cronometer foods (CUSTOM → FAVOURITES → COMMON_FOODS → SUPPLEMENTS → ALL).
- Selects a matching measure (exact match, contains, or falls back to grams `g`).
- Builds a multi-serving payload and posts to Cronometer `multi_add_serving`.
- Returns `200 OK` on success, `400 Bad Request` if Cronometer responds with `result=fail`.

## Quickstart
1. Ensure .NET 8 SDK is installed.
2. Run the API (Development enables Swagger UI):
   - Via VS/VS Code F5, or
   - `dotnet run` in `CronometerLogMealApi/`.
3. Open Swagger UI at the app URL (shown in console) and try `POST /api/Cronometer/Log-meal`.

### Example
Headers:
- `X-User-Id: 123456`
- `X-Auth-Token: <token>`

Request body:
```json
{
  "category": "lunch",
  "date": "2025-09-21T13:30:00",
  "logTime": true,
  "items": [
    { "quantity": 150, "unit": "g", "name": "Chicken breast" },
    { "quantity": 1, "unit": "cup", "name": "Brown rice" }
  ]
}
```

## Notes
- Base address for Cronometer client is configured to `https://mobile.cronometer.com/api/v2/` in `Program.cs`.
- The API inspects Cronometer response JSON for a `result: fail` to flag failures.
- You can customize measure matching in `CronometerController.GetSimilarMeasureId`.
