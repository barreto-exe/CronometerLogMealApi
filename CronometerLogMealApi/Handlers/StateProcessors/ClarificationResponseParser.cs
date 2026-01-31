using CronometerLogMealApi.Models;
using System.Text.RegularExpressions;

namespace CronometerLogMealApi.Handlers.StateProcessors;

/// <summary>
/// Parses clarification responses from user input.
/// </summary>
public static class ClarificationResponseParser
{
    /// <summary>
    /// Parses user response and maps answers to their corresponding clarifications.
    /// </summary>
    public static Dictionary<ClarificationItem, string> Parse(
        string userResponse, 
        List<ClarificationItem> pendingClarifications)
    {
        var result = new Dictionary<ClarificationItem, string>();
        
        if (string.IsNullOrWhiteSpace(userResponse) || pendingClarifications.Count == 0)
            return result;

        var response = userResponse.Trim();
        
        // Case 1: Single clarification - the entire response is the answer
        if (pendingClarifications.Count == 1)
        {
            result[pendingClarifications[0]] = response;
            return result;
        }

        // Case 2: Try newline separated responses
        var lines = response.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();

        if (lines.Count >= pendingClarifications.Count)
        {
            for (int i = 0; i < pendingClarifications.Count && i < lines.Count; i++)
            {
                var line = lines[i];
                // Remove numbered prefix
                var cleanedLine = Regex.Replace(line, @"^\d+[\.\)\:]\s*", "").Trim();
                if (!string.IsNullOrWhiteSpace(cleanedLine))
                {
                    result[pendingClarifications[i]] = cleanedLine;
                }
            }
            
            if (result.Count == pendingClarifications.Count)
                return result;
            
            result.Clear();
        }

        // Case 3: Try numbered responses
        var numberedPattern = new Regex(@"(\d+)[\.\)\:]\s*(.+?)(?=\s+\d+[\.\)\:]|$)", RegexOptions.IgnoreCase);
        var matches = numberedPattern.Matches(response);
        
        if (matches.Count > 0)
        {
            foreach (Match match in matches)
            {
                if (int.TryParse(match.Groups[1].Value, out int num) && 
                    num >= 1 && num <= pendingClarifications.Count)
                {
                    var answer = match.Groups[2].Value.Trim();
                    if (!string.IsNullOrWhiteSpace(answer))
                    {
                        result[pendingClarifications[num - 1]] = answer;
                    }
                }
            }
            
            if (result.Count > 0)
                return result;
        }

        // Case 4: Try comma/semicolon separated
        var separators = new[] { ',', ';' };
        var parts = response.Split(separators, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToList();

        if (parts.Count == pendingClarifications.Count)
        {
            for (int i = 0; i < parts.Count; i++)
            {
                result[pendingClarifications[i]] = parts[i];
            }
            return result;
        }

        // Case 5: Try to match by clarification type keywords
        foreach (var clarification in pendingClarifications)
        {
            var matchedAnswer = TryExtractAnswerByType(response, clarification);
            if (!string.IsNullOrEmpty(matchedAnswer))
            {
                result[clarification] = matchedAnswer;
            }
        }

        return result;
    }

    private static string? TryExtractAnswerByType(string response, ClarificationItem clarification)
    {
        var responseLower = response.ToLowerInvariant();
        
        switch (clarification.Type)
        {
            case ClarificationType.MissingSize:
                var sizeKeywords = new[] { 
                    "pequeño", "pequeña", "chico", "chica", 
                    "mediano", "mediana", "regular", 
                    "grande", "extra grande", "xl", 
                    "small", "medium", "large" 
                };
                foreach (var keyword in sizeKeywords)
                {
                    if (responseLower.Contains(keyword))
                        return keyword;
                }
                break;

            case ClarificationType.MissingWeight:
                var weightPattern = new Regex(
                    @"(\d+(?:\.\d+)?\s*(?:g|gr|gramos?|kg|ml|l|litros?|oz|onzas?|lb|libras?|tazas?|cucharadas?|cdas?|cups?|tbsp|tsp))",
                    RegexOptions.IgnoreCase);
                var match = weightPattern.Match(response);
                if (match.Success)
                    return match.Value.Trim();
                break;

            case ClarificationType.AmbiguousUnit:
                var unitKeywords = new[] { 
                    "sopera", "postre", "café", 
                    "cucharadita", "cucharada grande", 
                    "tbsp", "tsp", "tablespoon", "teaspoon" 
                };
                foreach (var keyword in unitKeywords)
                {
                    if (responseLower.Contains(keyword))
                        return keyword;
                }
                break;
        }

        return null;
    }
}
