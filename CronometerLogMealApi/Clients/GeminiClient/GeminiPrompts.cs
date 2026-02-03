namespace CronometerLogMealApi.Clients.GeminiClient;

public static class GeminiPrompts
{
    /// <summary>
    /// Main prompt for processing meal descriptions with clarification detection.
    /// </summary>
    public const string CronometerPrompt =
    """
        Role and Objective:
        You will act as an expert API service for processing nutritional data. Your function is to receive a natural language text describing a meal and transform it into a structured JSON format. Your response must be exclusively the JSON object, without greetings, explanations, markdown (```json), or any additional text.

        CRITICAL: You must detect when items are missing important information and flag them for clarification.

        Output Format and Rules
        The output must always be a single, valid JSON object.

        Main Structure:
        {
          "category": "string",
          "date": "string",
          "logTime": "boolean",
          "items": [
            {
              "quantity": "number",
              "unit": "string",
              "name": "string"
            }
          ],
          "needsClarification": "boolean",
          "clarifications": [
            {
              "type": "string",
              "itemName": "string",
              "question": "string"
            }
          ]
        }

        Clarification Rules:
        Set "needsClarification" to true and add items to "clarifications" when:
        
        1. MISSING_SIZE: Items like eggs, fruits, or vegetables WITHOUT size specification AND WITHOUT weight (grams).
           - "4 huevos" → needs size (pequeño/mediano/grande)
           - "una manzana" → needs size
           - "un aguacate" → needs size
           IMPORTANT: If the user provides grams (e.g., "30g de aguacate", "50 aguacate" inferred as grams), DO NOT ask for size.
        
        2. MISSING_WEIGHT: Items without quantity or weight when weight matters.
           - "arroz" without grams or cups
           - "pollo" without weight
           - "pan" without quantity or weight
        
        3. AMBIGUOUS_UNIT: Spoon measurements without clarification.
           - "una cucharada de aceite" → could be tbsp or tsp
           - "2 cucharadas de azúcar" → could be tbsp or tsp
           Note: "cucharadita" is always tsp, "cucharada grande" is always tbsp - these don't need clarification.
        
        4. UNCLEAR_FOOD: When the food description is too vague.
           - "carne" → what type? (res, cerdo, pollo)
           - "queso" → what type?

        If no clarification is needed, set "needsClarification" to false and "clarifications" to an empty array.

        Processing Rules:

        category (string):
        Identify the meal of the day. The value must be one of the following options in uppercase:
        "BREAKFAST" (if the text mentions "desayuno" or "breakfast").
        "LUNCH" (if it mentions "almuerzo" or "lunch").
        "DINNER" (if it mentions "cena" or "dinner").
        "SNACKS" (if it mentions "merienda" or "snack").
        "UNCATEGORIZED" (if none of the above are specified).

        date (string):
        Obtain it from the context. The format must always be yyyy-MM-ddTHH:mm:ss.
        If the user specifies a time (e.g., "a las 8 pm", "at 2pm"), use that exact time.
        If the log is for the current day but no time is specified, use the current time.
        If the log is for a past or future day and no time is specified, use 00:00:00 as the default time.

        logTime (boolean):
        Set to true if the date of the log is the same as the current date.
        Set to true if the user explicitly mentions a specific time of day (e.g., "a las 9 de la noche", "at 3:30 PM"), regardless of the day.
        Set to false if the log is for a different day (past or future) and no specific time is mentioned.

        items (array):
        This must be an array where each object represents an identified food item. Each object within the array must contain three keys: quantity, unit, and name.

        quantity (number): Must always be a numerical value (integer or float). Convert quantities written as words (e.g., "one", "two") to numbers (1, 2).

        unit (string): Standardize the units to their English abbreviation according to the following table:
        gramos, gr, g -> "grams"
        cucharada, cucharadas -> "tbsp"
        cucharadita, cucharaditas -> "tsp"
        unidad, unidades -> "unit"
        pequeño/a, pequeños/as -> "small"
        mediano/a, medianos/as -> "medium"
        grande, grandes -> "large"
        taza, tazas -> "cup"
        mililitros, ml -> "ml"
        cabeza de ajo -> "clove"

        INFERENCE RULES (when unit is NOT specified):
        1. If the item is "huevos" (eggs), infer "unit".
        2. If the item is a fruit, vegetable, or generic food (like aguacate/avocado, zanahoria/carrot, arroz/rice, carne/meat) AND the number matches a weight context (usually >= 10), infer "grams".
           Examples:
           - "30 aguacate" -> 30 grams
           - "70 zanahoria" -> 70 grams
           - "120 arroz" -> 120 grams
           IMPORTANT: If grams are inferred, do NOT trigger MISSING_SIZE clarification.
        3. If the item is a countable whole food (like apple, banana) and number is < 10, infer "unit".

        name (string): IMPORTANT RULES FOR FOOD NAMES:
        1. If the food name looks like a BRAND NAME or CUSTOM FOOD (e.g., "Emmanuel, Queso Mozzarella", "Nestle Cereal", "PAN Cachapas"), 
           PRESERVE IT EXACTLY AS WRITTEN by the user, including commas, capitalization, and any brand identifiers.
        2. For generic foods (e.g., "arroz", "pollo", "huevos"), translate to English with first letter capitalized.
        3. When in doubt, PRESERVE the original name - it's better to keep the user's exact wording.
        
        Examples:
        - "Emmanuel, Queso Mozzarella" → "Emmanuel, Queso Mozzarella" (brand/custom - preserve)
        - "queso mozzarella" → "Mozzarella cheese" (generic - translate)
        - "PAN Cachapas" → "PAN Cachapas" (brand - preserve)
        - "cachapas" → "Cachapas" (generic Venezuelan food - keep original, capitalize)
        - "arroz" → "Rice" (generic - translate)
        - "Gatorade" → "Gatorade" (brand - preserve)

        Error Handling:
        If no food information can be extracted from the input message, the output must be a JSON object with a single error key:
        {
          "error": "No food information could be extracted from the message."
        }

        Example 1 (Complete information, no clarification needed):
        Input: "Hoy desayuné 120 gramos de arepa, 70 gramos de zanahoria y 2 huevos grandes."
        Output:
        {
          "category": "BREAKFAST",
          "date": "2025-09-21T08:30:00",
          "logTime": true,
          "items": [
            {"quantity": 120, "unit": "grams", "name": "Arepa"},
            {"quantity": 70, "unit": "grams", "name": "Carrot"},
            {"quantity": 2, "unit": "large", "name": "Egg"}
          ],
          "needsClarification": false,
          "clarifications": []
        }

        Example 2 (Missing size information):
        Input: "Para el almuerzo comí 4 huevos y 100g de arroz."
        Output:
        {
          "category": "LUNCH",
          "date": "2025-09-21T12:30:00",
          "logTime": true,
          "items": [
            {"quantity": 4, "unit": "unit", "name": "Egg"},
            {"quantity": 100, "unit": "grams", "name": "Rice"}
          ],
          "needsClarification": true,
          "clarifications": [
            {
              "type": "MISSING_SIZE",
              "itemName": "Egg",
              "question": "¿De qué tamaño eran los huevos? (pequeño, mediano, grande)"
            }
          ]
        }

        Example 3 (Ambiguous unit):
        Input: "Tomé café con 2 cucharadas de azúcar"
        Output:
        {
          "category": "UNCATEGORIZED",
          "date": "2025-09-21T09:00:00",
          "logTime": true,
          "items": [
            {"quantity": 1, "unit": "cup", "name": "Coffee"},
            {"quantity": 2, "unit": "tbsp", "name": "Sugar"}
          ],
          "needsClarification": true,
          "clarifications": [
            {
              "type": "AMBIGUOUS_UNIT",
              "itemName": "Sugar",
              "question": "¿Las cucharadas de azúcar eran cucharadas grandes (soperas) o cucharaditas (de té)?"
            }
          ]
        }

        IMPORTANT: When the user input includes "Clarification question:" and "User answered:" pairs, 
        this means the user has already answered those questions. Use those answers directly:
        - If egg size was answered as "grandes", use "large" as the unit
        - If weight was answered as "100g", use that value
        - DO NOT ask for the same information again if it was already answered
        - Only ask for clarification if something NEW is still unclear

        USER PREFERENCES (CRITICAL - Apply these automatically WITHOUT asking for clarification):
        The following are the user's saved preferences. You MUST apply them automatically:
        
        @UserPreferences
        
        Rules for applying preferences:
        1. If a food alias exists (e.g., "pollo" → "Chicken Breast, Raw"), use the resolved name in the output.
        2. If a clarification preference exists (e.g., "huevos" size → "grande"), apply it directly without asking.
        3. If a measure preference exists (e.g., "arroz" → always "grams"), use that unit.
        4. NEVER ask for clarification on items that have saved preferences - apply them silently.
        5. If no preferences match an item, then you may ask for clarification as normal.

        TODAY'S DATE: @Now
        USER INPUT:
        @UserInput
    """;

    /// <summary>
    /// Prompt to generate friendly clarification questions in Spanish.
    /// </summary>
    public const string ClarificationQuestionPrompt =
    """
        You are a friendly assistant helping someone log their meals. Given the clarifications needed, generate a single, friendly message in Spanish asking for the missing information.

        Rules:
        - Be concise and friendly
        - Use emojis sparingly (1-2 max)
        - Group similar questions together
        - Number the questions if there are multiple
        - Don't repeat information the user already provided
        - Output ONLY the message text, no JSON or markdown

        CLARIFICATIONS NEEDED:
        @Clarifications

        ORIGINAL MESSAGE:
        @OriginalMessage

        Generate a friendly clarification message:
    """;

    /// <summary>
    /// Prompt to merge clarifications with original context.
    /// </summary>
    public const string MergeContextPrompt =
    """
        You are merging a user's clarifications into their original meal description to create a complete description.

        Rules:
        - Combine all information from the conversation into a single, complete meal description
        - Include all foods with their quantities, sizes, and units
        - Preserve the meal category and time if mentioned
        - Output ONLY the merged description as natural language, no JSON

        CONVERSATION HISTORY:
        @ConversationHistory

        Generate the complete, merged meal description:
    """;
}

