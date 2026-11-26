using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Beta.Messages;

namespace DukkanPaneli.Api.Features.Assistant;

/// <summary>
/// Claude API bağlantısı (resmî Anthropic C# kütüphanesi).
/// Anahtar sunucuda durur: <c>ANTHROPIC_API_KEY</c> ortam değişkeni ya da <c>Anthropic:ApiKey</c> ayarı.
/// Model: <c>Assistant:Model</c> (varsayılan claude-opus-5-5), düşünme derinliği: <c>Assistant:Effort</c> (varsayılan medium).
/// </summary>
public sealed class ClaudeAssistantModel : IAssistantModel
{
    private readonly AnthropicClient? _client;
    private readonly string _effort;

    public ClaudeAssistantModel(IConfiguration config)
    {
        var key = config["Anthropic:ApiKey"] is { Length: > 0 } k ? k : Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        if (!string.IsNullOrWhiteSpace(key)) _client = new AnthropicClient { ApiKey = key };
        ModelName = config["Assistant:Model"] is { Length: > 0 } m ? m : "claude-opus-5-5";
        _effort = config["Assistant:Effort"] is { Length: > 0 } e ? e : "medium";
    }

    public bool IsConfigured => _client is not null;
    public string ModelName { get; }

    public async Task<AssistantModelReply> CompleteAsync(string system, IReadOnlyList<AssistantTurn> turns, CancellationToken ct)
    {
        if (_client is null) throw new AssistantUnavailableException("Asistan için API anahtarı tanımlı değil.", StatusCodes.Status503ServiceUnavailable);

        List<BetaMessageParam> messages = [.. turns.Select(t => t.Role == "assistant"
            ? (BetaMessageParam)new BetaAssistantMessageParam { Content = t.Text }
            : new BetaUserMessageParam { Content = t.Text })];

        try
        {
            var response = await _client.Beta.Messages.Create(new MessageCreateParams
            {
                Model = ModelName,
                MaxTokens = 8000,
                System = system,
                Messages = messages,
                OutputConfig = new BetaOutputConfig { Effort = _effort switch { "low" => Effort.Low, "high" => Effort.High, _ => Effort.Medium } },
                // Model nadiren bir soruyu güvenlik sınıflandırıcısı yüzünden reddedebilir; bu durumda
                // sunucu aynı isteği uygun bir yedek modele yönlendirir ("default" = kategoriye göre otomatik seçim).
                Betas = ["server-side-fallback-2026-07-01"],
                Fallbacks = new Default(),
            }, ct);

            if (response.StopReason == "refusal") return new AssistantModelReply("", Refused: true);
            var text = string.Join("\n", response.Content.Select(b => b.TryPickText(out var t) ? t.Text : null).Where(t => t is not null));
            return new AssistantModelReply(text);
        }
        catch (AnthropicUnauthorizedException)
        {
            throw new AssistantUnavailableException("API anahtarı geçersiz ya da iptal edilmiş. Sunucudaki ANTHROPIC_API_KEY değerini kontrol edin.", StatusCodes.Status503ServiceUnavailable);
        }
        catch (AnthropicRateLimitException)
        {
            throw new AssistantUnavailableException("Çok fazla istek ya da aylık harcama sınırına ulaşıldı. Biraz sonra tekrar deneyin.", StatusCodes.Status429TooManyRequests);
        }
        catch (Anthropic5xxException)
        {
            throw new AssistantUnavailableException("Yapay zekâ servisi şu an yanıt vermiyor. Biraz sonra tekrar deneyin.", StatusCodes.Status503ServiceUnavailable);
        }
        catch (AnthropicIOException)
        {
            throw new AssistantUnavailableException("Yapay zekâ servisine bağlanılamadı. Sunucunun internet bağlantısını kontrol edin.", StatusCodes.Status503ServiceUnavailable);
        }
        catch (AnthropicApiException e)
        {
            throw new AssistantUnavailableException($"Yapay zekâ servisi isteği kabul etmedi ({e.Message}).", StatusCodes.Status502BadGateway);
        }
    }
}
