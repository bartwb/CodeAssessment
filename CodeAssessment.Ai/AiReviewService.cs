using System.Text.Json;
using OpenAI.Chat;
using CodeAssessment.Shared;

namespace CodeAssessment.Ai;

public class AiReviewService : IAiReviewService
{
    public async Task<AiReviewResult> ReviewAsync(CodeRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Code))
            throw new ArgumentException("Code is leeg.", nameof(req));

        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("OPENAI_API_KEY is niet ingesteld.");

        var client = new ChatClient(
            model: "gpt-4o",
            apiKey: apiKey
        );

        string systemPrompt = GetSystemPrompt();

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(systemPrompt),
            new UserChatMessage(req.Code)
        };

        var options = new ChatCompletionOptions
        {
            Temperature = 0,
            ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat()
        };

        ChatCompletion completion = await client.CompleteChatAsync(messages, options);
        string reviewContent = completion.Content[0].Text ?? string.Empty;

        var result = new AiReviewResult
        {
            RawJson = reviewContent
        };

        try
        {
            using var json = JsonDocument.Parse(reviewContent);
            var root = json.RootElement;

            // final_score
            if (root.TryGetProperty("final_score", out var finalScoreProp) &&
                finalScoreProp.ValueKind == JsonValueKind.Number &&
                finalScoreProp.TryGetInt32(out var score))
            {
                result.FinalScore = score;
            }

            // general_feedback
            if (root.TryGetProperty("general_feedback", out var feedbackProp) &&
                feedbackProp.ValueKind == JsonValueKind.String)
            {
                result.GeneralFeedback = feedbackProp.GetString() ?? "";
            }

            // issues
            if (root.TryGetProperty("issues", out var issuesProp) &&
                issuesProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var issueEl in issuesProp.EnumerateArray())
                {
                    var issue = new AiIssue();

                    if (issueEl.TryGetProperty("line_start", out var ls) &&
                        ls.ValueKind == JsonValueKind.Number &&
                        ls.TryGetInt32(out var lsInt))
                        issue.LineStart = lsInt;

                    if (issueEl.TryGetProperty("line_end", out var le) &&
                        le.ValueKind == JsonValueKind.Number &&
                        le.TryGetInt32(out var leInt))
                        issue.LineEnd = leInt;

                    if (issueEl.TryGetProperty("severity", out var sev) &&
                        sev.ValueKind == JsonValueKind.String)
                        issue.Severity = sev.GetString() ?? "";

                    if (issueEl.TryGetProperty("suggestion", out var sug) &&
                        sug.ValueKind == JsonValueKind.String)
                        issue.Suggestion = sug.GetString() ?? "";

                    result.Issues.Add(issue);
                }
            }
        }
        catch (JsonException)
        {
            // Parsing mislukt: we laten RawJson staan en laten de structured velden zo goed mogelijk leeg.
        }

        return result;
    }

    private static string GetSystemPrompt()
    {
        return """
        Je bent een expert C# .NET senior ontwikkelaar. Jouw taak is om een VOLLEDIGE review van de aangeleverde C#-code te geven.

        Je beoordeelt de code op ALLE relevante aspecten:
        1.  **Correctheid & Logica:** Doet de code wat het lijkt te bedoelen? Zitten er bugs in?
        2.  **Prestatie & Efficiëntie:** Zijn er onnodige 'blocking' calls, 'dure' operaties in loops (zoals string-concatenatie), of geheugenproblemen?
        3.  **Veiligheid & Robuustheid:** Worden inputs gevalideerd? Worden exceptions correct afgehandeld (`try-catch`)? Zijn er security-risico's (zoals hardcoded secrets)?
        4.  **Onderhoudbaarheid & Best Practices:** Is de code 'clean'? Worden SOLID-principes gevolgd? Is de naamgeving duidelijk?
        5.  **Stijl & Leesbaarheid:** Is de code onnodig complex? Is de stijl consistent? Worden 'language features' (zoals LINQ of `using`) correct toegepast?

        Nadat je de code volledig hebt geanalyseerd, geef je een **eindscore van 1 (zeer slecht) tot 10 (perfecte code)**.

        Je MOET je antwoord formatteren als een **enkel, valide JSON-object**.
        Het formaat van het JSON-object is:
        {
          "final_score": (int, een getal van 1 tot 10),
          "general_feedback": (string, "Een algemene samenvatting van de codekwaliteit, de plus- en minpunten."),
          "issues": [
            {
              "line_start": (int),
              "line_end": (int),
              "severity": "Info" | "Warning" | "Error",
              "suggestion": (string - "Een duidelijke, concrete uitleg van het probleem en hoe het op te lossen.")
            }
          ]
        }

        Als je absoluut geen suggesties hebt, geef dan een lege array `[]` terug voor 'issues' en een 'final_score' van 10.
        Geef GEEN commentaar of extra tekst buiten het JSON-object.
        """;
    }
}
