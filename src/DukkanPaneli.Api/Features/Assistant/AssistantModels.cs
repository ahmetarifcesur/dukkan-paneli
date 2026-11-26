namespace DukkanPaneli.Api.Features.Assistant;

/// <param name="Role">"user" (sahibin sorusu) ya da "assistant" (önceki cevap).</param>
public sealed record AssistantTurn(string Role, string Text);

/// <param name="History">Aynı sohbetteki önceki sorular ve cevaplar (en eskiden yeniye).</param>
public sealed record AskRequest(string? Question, List<AssistantTurn>? History);

public sealed record AskResponse(string Answer);

/// <param name="Enabled">Sunucuda API anahtarı tanımlı mı.</param>
public sealed record AssistantStatusDto(bool Enabled, string Model);

/// <summary>Modele gönderilen dükkan özeti: metin ve içinde kullanılan takma adlar.</summary>
public sealed record ShopSnapshot(string Text, Pseudonyms Names);

/// <summary>
/// Dil modeli bağlantısı. Gerçek uygulamada Claude API'yi çağırır (<see cref="ClaudeAssistantModel"/>);
/// testlerde sahte bir sınıfla değiştirilir, böylece testler internete çıkmaz ve para harcamaz.
/// </summary>
public interface IAssistantModel
{
    bool IsConfigured { get; }
    string ModelName { get; }

    /// <param name="turns">Sırayla user/assistant mesajları; ilki ve sonuncusu user.</param>
    Task<AssistantModelReply> CompleteAsync(string system, IReadOnlyList<AssistantTurn> turns, CancellationToken ct);
}

/// <param name="Refused">Model güvenlik nedeniyle yanıt vermediyse true.</param>
public sealed record AssistantModelReply(string Text, bool Refused = false);

/// <summary>Dil modeli çağrısı başarısız olduğunda kullanıcıya gösterilecek Türkçe mesajla birlikte fırlatılır.</summary>
public sealed class AssistantUnavailableException(string message, int status) : Exception(message)
{
    public int Status { get; } = status;
}
