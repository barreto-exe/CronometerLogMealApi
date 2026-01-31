namespace CronometerLogMealApi.Constants;

/// <summary>
/// Contains all Telegram bot messages in Spanish for easy modification.
/// All messages are organized by category for better maintainability.
/// </summary>
public static class TelegramMessages
{
    /// <summary>
    /// Messages related to authentication and login.
    /// </summary>
    public static class Auth
    {
        public const string LoginRequired = "⚠️ Primero debes iniciar sesión con:\n<b>/login &lt;email&gt; &lt;password&gt;</b>";
        public const string InvalidLoginFormat = "Formato de logueo inválido. Use: <b>/login &lt;email&gt; &lt;password&gt;</b>";
        public const string LoggingIn = "🔐 Iniciando sesión...";
        public const string LoginFailed = "❌ Error de autenticación. Por favor, verifique sus credenciales.";
        public const string LoginSuccess = "✅ <b>Inicio de sesión exitoso.</b>\n\n" +
            "Ahora puedes registrar tus comidas usando el comando /start.\n" +
            "Usa /preferences para ver y gestionar tus preferencias guardadas.";
        public const string NotAuthenticated = "No estás autenticado. Por favor, inicia sesión usando el comando:\n" +
            "<b>/login &lt;email&gt; &lt;password&gt;</b>";
    }

    /// <summary>
    /// Messages related to session management.
    /// </summary>
    public static class Session
    {
        public const string Expired = "⏰ Tu sesión anterior expiró por inactividad. Usa /start para iniciar una nueva.";
        public const string AlreadyActive = "⚠️ Ya tienes una sesión activa. Usa /cancel para cancelarla primero.";
        public const string AlreadyActiveWithSave = "⚠️ Ya tienes una sesión activa. Usa /cancel para cancelarla primero, o usa /save para guardar los cambios pendientes.";
        public const string NoActiveSession = "No hay ninguna sesión activa para cancelar.";
        public const string Cancelled = "❌ Sesión cancelada. Usa /start para iniciar una nueva.";
        public const string NoSessionToSave = "No hay una sesión activa para guardar.";
        public const string NoPendingChanges = "⚠️ No hay cambios pendientes de confirmación. Usa /start para iniciar.";
        public const string NoValidatedData = "❌ Error interno: No hay datos de comida validados. Por favor inicia de nuevo con /start.";
        public const string UseStartToBegin = "💡 Para registrar una comida, usa el comando /start para iniciar una nueva sesión.";
        public const string UseStartForNew = "💡 Usa /start para iniciar una nueva sesión de registro.";
    }

    /// <summary>
    /// Messages related to meal registration.
    /// </summary>
    public static class Meal
    {
        public const string NewSessionStarted = "🍽️ <b>Nueva sesión de registro iniciada</b>\n\n" +
            "Describe tu comida incluyendo:\n" +
            "• 📅 Tipo de comida (desayuno, almuerzo, cena, merienda)\n" +
            "• ⚖️ Cantidades y pesos (ej: 100g de arroz)\n" +
            "• 📏 Tamaños cuando aplique (huevos pequeños, medianos, grandes)\n\n" +
            "💡 <i>Tip: Entre más detallado sea tu mensaje, menos preguntas tendré que hacerte.</i>\n\n" +
            "Usa /cancel para cancelar en cualquier momento.";
        
        public const string ProcessingMessage = "⏳ Procesando tu mensaje...";
        public const string ProcessingResponse = "⏳ Procesando tu respuesta...";
        public const string Processing = "⏳ Procesando...";
        public const string StillProcessing = "⏳ Aún estoy procesando tu solicitud anterior. Por favor, espera un momento.";
        public const string ValidatingWithCronometer = "🔍 Validando con Cronometer...";
        public const string Saving = "💾 Guardando cambios...";
        
        public const string SaveSuccess = "✅ <b>¡Guardado exitoso!</b>\n\nTu comida ha sido registrada.";
        public const string SaveError = "❌ Error al guardar en Cronometer.";
        public const string SaveRetryError = "❌ Ocurrió un error al guardar. Intenta /save nuevamente.";
        
        public const string ProcessingError = "❌ Ocurrió un error al procesar tu mensaje. Por favor, intenta nuevamente.";
        public const string ClarificationError = "❌ Ocurrió un error al procesar tu respuesta. Por favor, intenta nuevamente.";
        public const string ChangeError = "❌ Ocurrió un error al procesar tu cambio. Intenta nuevamente.";
        
        public const string NeedsClarificationPrefix = "🤔 Necesito un poco más de información:\n\n";
        public const string StillNeedsClarification = "🤔 Aún necesito más información:\n\n";
        public const string ProcessingChanges = "🔄 Entendido, vamos a corregir. Procesando tus cambios...";

        public static string FormatNotFoundItems(IEnumerable<string> items)
        {
            var itemsList = string.Join("\n", items.Select(i => $"• <b>{i}</b>"));
            return $"⚠️ <b>No encontré estos alimentos:</b>\n\n{itemsList}\n\n" +
                   "Por favor, dame nombres alternativos (ej: \"pollo\" -> \"pechuga de pollo\").\n\n" +
                   "💡 Tip: Usa /search [nombre] para buscar manualmente.";
        }

        public static string FormatConfirmation(string time, string category, string itemsSummary, bool hasMemoryItems)
        {
            var memoryLegend = hasMemoryItems ? "🧠 = reconocido desde tu memoria\n\n" : "";
            return $"💾 Estás a punto de registrar:\n\n" +
                   $"<b>Hora:</b> {time}\n" +
                   $"<b>Tipo:</b> {category}\n\n" +
                   $"<b>Alimentos:</b>\n{itemsSummary}\n\n" +
                   memoryLegend +
                   "¿Deseas hacer algún cambio?\n" +
                   "• Responde con el número del item para <b>buscar alternativas</b>\n" +
                   "• Usa <b>/save</b> para guardar los cambios";
        }

        public static string FormatDescriptionError(string errorMessage)
        {
            return $"❌ {errorMessage}\n\nPor favor, intenta describir tu comida nuevamente.";
        }

        public static string FormatClarificationResponseError(string errorMessage)
        {
            return $"❌ {errorMessage}\n\nPor favor, intenta responder nuevamente.";
        }
    }

    /// <summary>
    /// Messages related to OCR (photo processing).
    /// </summary>
    public static class Ocr
    {
        public const string ProcessingPhoto = "📷 Procesando tu foto...";
        public const string PhotoGetError = "❌ No pude obtener la foto. Por favor, intenta enviarla de nuevo.";
        public const string NoTextDetected = "❌ No pude leer texto en la imagen. Asegúrate de que el texto sea legible o envía un mensaje de texto describiendo tu comida.";
        public const string NoOcrTextSaved = "❌ No hay texto OCR guardado. Por favor, envía una foto nuevamente.";
        public const string ProcessingOcrError = "❌ Ocurrió un error al procesar la imagen. Por favor, intenta de nuevo o envía un mensaje de texto.";
        public const string ContinueOnlyAfterPhoto = "⚠️ Este comando solo se puede usar después de enviar una foto para confirmar el texto detectado.";
        public const string OcrProcessingError = "❌ Ocurrió un error al procesar. Por favor, intenta de nuevo.";

        public static string FormatDetectedTextOnly(string extractedText)
        {
            return $"<pre>{extractedText}</pre>";
        }

        public const string TextDetectedInstructions = "📝 <b>Texto detectado arriba ☝️</b>\n\n" +
            "✏️ Si hay algún error, escribe las correcciones.\n" +
            "✅ Si todo está correcto, usa /continue para continuar.";
    }

    /// <summary>
    /// Messages related to preferences and aliases.
    /// </summary>
    public static class Preferences
    {
        public const string ServiceNotAvailable = "⚠️ El servicio de memoria no está disponible.";
        public const string NoAliasesToDelete = "No tienes alias para eliminar. Usa /preferences para volver al menú.";
        public const string ExitedPreferences = "👋 Saliste del menú de preferencias. Usa /start para registrar comidas.";
        public const string InvalidOption = "Por favor, responde con 1, 2 o 3.";
        public const string InvalidNumber = "Por favor, responde con un número válido o /cancel para salir.";
        public const string Done = "✅ Listo. Usa /start para registrar otra comida.";
        public const string NoPreferencesSaved = "👍 Entendido. No se guardaron preferencias.\nUsa /start para registrar otra comida.";
        public const string InvalidMemoryResponse = "Por favor, responde 'si', 'no', o los números de las preferencias a guardar (ej: 1,3).";
        
        public const string CreateAliasPrompt = "📝 <b>Crear nuevo alias</b>\n\n" +
            "Escribe el término que usas normalmente.\n" +
            "Ejemplo: \"pollo\", \"arroz integral\", \"mi proteina\"";
        
        public const string SearchPrompt = "🔍 Buscando...";
        public const string SearchError = "❌ Error al buscar. Intenta de nuevo.";
        public const string NoSearchResults = "❌ No encontré resultados. Intenta con otro término de búsqueda:";
        public const string FoodInfoError = "❌ Error al obtener información del alimento.";
        
        public static string FormatTermSaved(string term)
        {
            return $"Término guardado: <b>{term}</b>\n\nAhora escribe el nombre del alimento a buscar en Cronometer:";
        }

        public static string FormatAliasSaved(string inputTerm, string resolvedName)
        {
            return $"✅ <b>Alias guardado!</b>\n\n\"{inputTerm}\" → {resolvedName}\n\nUsa /preferences para ver todos tus alias.";
        }

        public static string FormatAliasDeleted(string inputTerm, string resolvedName)
        {
            return $"🗑️ Alias eliminado: \"{inputTerm}\" → {resolvedName}\n\nUsa /preferences para volver al menú.";
        }

        public static string FormatPreferencesMenu(IEnumerable<(string Term, string FoodName, int UseCount)> aliases)
        {
            var aliasList = aliases.ToList();
            var msg = "⚙️ <b>Gestión de Preferencias</b>\n\n";

            if (aliasList.Any())
            {
                msg += "<b>Tus alias guardados:</b>\n";
                msg += string.Join("\n", aliasList.Take(10).Select((a, i) =>
                    $"{i + 1}. \"{a.Term}\" → {a.FoodName} ({a.UseCount}x)"));

                if (aliasList.Count > 10)
                    msg += $"\n... y {aliasList.Count - 10} más";

                msg += "\n\n";
            }
            else
            {
                msg += "<i>No tienes alias guardados todavía.</i>\n\n";
            }

            msg += "<b>Opciones:</b>\n" +
                   "1️⃣ <b>Crear</b> nuevo alias\n" +
                   "2️⃣ <b>Eliminar</b> un alias\n" +
                   "3️⃣ <b>Salir</b>\n\n" +
                   "Responde con el número de la opción.";

            return msg;
        }

        public static string FormatDeleteAliasMenu(IEnumerable<(string Term, string FoodName)> aliases)
        {
            var aliasList = aliases.ToList();
            return "🗑️ <b>Eliminar alias</b>\n\n" +
                   "Selecciona el número del alias a eliminar:\n\n" +
                   string.Join("\n", aliasList.Take(15).Select((a, i) =>
                       $"{i + 1}. \"{a.Term}\" → {a.FoodName}"));
        }

        public static string FormatSearchResults(IEnumerable<(string Name, string Tab)> results)
        {
            var resultList = results.ToList();
            return "📋 <b>Resultados de búsqueda:</b>\n\n" +
                   string.Join("\n", resultList.Take(10).Select((r, i) =>
                       $"{i + 1}. {r.Name} <i>[{r.Tab}]</i>")) +
                   "\n\nResponde con el número para seleccionar, o escribe otro término para buscar de nuevo.";
        }

        public static string FormatMemoryConfirmation(IEnumerable<(string OriginalTerm, string ResolvedName)> learnings)
        {
            var learningsList = learnings.ToList();
            return "✅ <b>¡Guardado exitoso!</b>\n\n" +
                   "🧠 <b>¿Quieres que recuerde estas asociaciones?</b>\n\n" +
                   string.Join("\n", learningsList.Select((l, i) =>
                       $"{i + 1}. \"{l.OriginalTerm}\" → <b>{l.ResolvedName}</b>")) +
                   "\n\n• Responde <b>si</b> para guardar todas\n" +
                   "• Responde con los números (ej: 1,3) para guardar solo algunas\n" +
                   "• Responde <b>no</b> para no guardar ninguna";
        }

        public static string FormatPreferencesSaved(int count)
        {
            return $"🧠 <b>¡{count} preferencia(s) guardada(s)!</b>\n\n" +
                   "La próxima vez que uses estos términos, los reconoceré automáticamente.\n" +
                   "Usa /start para registrar otra comida o /preferences para ver tus preferencias.";
        }

        public static string FormatAutoAppliedPreferences(IEnumerable<string> preferences)
        {
            var prefList = string.Join(", ", preferences);
            return $"🧠 Usando tus preferencias guardadas ({prefList})...";
        }
    }

    /// <summary>
    /// Messages related to search functionality.
    /// </summary>
    public static class Search
    {
        public const string Usage = "Uso: /search [nombre del alimento]\nEjemplo: /search chicken breast";
        public const string Searching = "🔍 Buscando...";
        public const string Error = "❌ Error al buscar. Intenta de nuevo.";
        public const string AlternativesError = "❌ Error al buscar alternativas. Intenta de nuevo.";
        public const string NoAlternatives = "No hay alternativas disponibles. Intenta escribir un nombre diferente.";

        public static string FormatNoResults(string query)
        {
            return $"❌ No encontré resultados para \"{query}\".";
        }

        public static string FormatResults(string query, IEnumerable<(string Name, string Tab, double Score)> results)
        {
            var resultList = results.ToList();
            return $"📋 <b>Resultados para \"{query}\":</b>\n\n" +
                   string.Join("\n", resultList.Take(10).Select((r, i) =>
                       $"{i + 1}. {r.Name} <i>[{r.Tab}]</i> (Score: {r.Score:F2})"));
        }

        public static string FormatSearchingAlternatives(string itemName)
        {
            return $"🔍 Buscando alternativas para: <b>{itemName}</b>...";
        }

        public static string FormatAlternatives(string originalName, string currentName, long currentId,
            IEnumerable<(string Name, string Tab, long Id)> alternatives)
        {
            var altList = alternatives.ToList();
            return $"📋 <b>Alternativas para \"{originalName}\":</b>\n" +
                   $"(Actualmente: {currentName})\n\n" +
                   string.Join("\n", altList.Take(10).Select((a, i) =>
                   {
                       var current = a.Id == currentId ? " ✓" : "";
                       return $"{i + 1}. {a.Name} <i>[{a.Tab}]</i>{current}";
                   })) +
                   "\n\nResponde con el número para seleccionar, o /cancel para mantener el actual.";
        }
    }

}
