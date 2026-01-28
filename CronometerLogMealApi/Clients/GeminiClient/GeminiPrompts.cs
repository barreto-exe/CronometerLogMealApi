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
        
        1. MISSING_SIZE: Items like eggs, fruits, or vegetables without size specification.
           - "4 huevos" → needs size (pequeño/mediano/grande)
           - "una manzana" → needs size
           - "un aguacate" → needs size
        
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

        name (string): The name of the food in English with the first letter capitalized and the rest in lowercase.

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

    /// <summary>
    /// Prompt for OCR analysis of handwritten meal log images.
    /// </summary>
    public const string ImageOcrPrompt =
    """
        Eres un asistente experto en transcribir notas de comida escritas a mano en español.

        CONTEXTO:
        Esta imagen muestra un cuaderno donde una cocinera anota las comidas diarias para diferentes personas.
        La información típicamente está organizada por:
        - Fecha (formato DD/MM/YY en la esquina)
        - Persona (ej: Luis, Lioska, Lio)
        - Tipo de comida (Desayuno, Almuerzo, Cena, Merienda)
        - Items de comida con cantidades (ej: "70g de zanahoria", "4 Huevos")

        INSTRUCCIONES:
        1. Transcribe TODO el contenido visible de la imagen con el mayor detalle posible
        2. Identifica las diferentes personas mencionadas
        3. Identifica los tipos de comida disponibles (Desayuno, Almuerzo, etc.)
        4. Identifica la fecha si está visible
        5. Para texto ilegible, usa tu mejor interpretación y márcalo en uncertainItems
        6. Organiza los items en secciones por persona y tipo de comida

        REGLAS PARA INTERPRETAR TEXTO ILEGIBLE:
        - Si ves algo como "gramolei" probablemente es "granola"
        - Si ves "brocoli" sin tilde está bien
        - Los números suelen estar seguidos de "g" (gramos)
        - "cant" probablemente es "cantidad" o el número de unidades

        OUTPUT FORMAT (JSON únicamente, sin markdown):
        {
          "transcription": "transcripción completa del texto visible",
          "date": "fecha en formato yyyy-MM-dd si está visible, null si no",
          "people": ["lista de personas identificadas"],
          "mealTypes": ["tipos de comida identificados en español"],
          "sections": [
            {
              "person": "nombre de la persona",
              "mealType": "tipo de comida (Desayuno, Almuerzo, etc.)",
              "items": [
                {
                  "raw": "texto original como aparece",
                  "quantity": 70,
                  "unit": "g",
                  "name": "zanahoria"
                }
              ]
            }
          ],
          "uncertainItems": [
            {
              "original": "lo que parece decir el texto ilegible",
              "suggestion": "interpretación sugerida",
              "question": "¿Quisiste escribir X en lugar de Y?"
            }
          ],
          "needsClarification": true/false,
          "clarificationQuestions": [
            "¿De qué persona deseas registrar la comida? (Luis, Lioska)",
            "¿Qué tipo de comida deseas registrar? (Desayuno, Almuerzo)"
          ]
        }

        CUÁNDO PEDIR CLARIFICACIÓN:
        - Si hay múltiples personas, pregunta cuál registrar
        - Si hay múltiples tipos de comida, pregunta cuál registrar
        - Si hay texto muy ilegible, sugiere interpretaciones
        - Si hay múltiples fechas visibles, pregunta cuál usar

        TODAY'S DATE: @Now

        Analiza la imagen y responde SOLO con el JSON, sin explicaciones adicionales.
    """;
}


