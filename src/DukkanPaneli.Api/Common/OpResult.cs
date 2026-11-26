using DukkanPaneli.Api.Data;

namespace DukkanPaneli.Api.Common;

/// <summary>
/// Değişiklik yapan bir servis metodunun sonucu: ya başarılı (geçmiş kaydı + isteğe bağlı veri)
/// ya da başarısız (kullanıcıya gösterilecek hata mesajı + HTTP durum kodu).
///
/// Neden exception değil? Doğrulama hataları (ör. "Yetersiz stok") beklenen durumlardır; bunları
/// normal dönüş değeri olarak ele almak akışı okunur kılar. Exception gerçekten beklenmeyen hatalar içindir.
/// </summary>
public sealed class OpResult
{
    public AuditLog? Log { get; private init; }
    public object? Data { get; private init; }
    public string? Error { get; private init; }
    public int Status { get; private init; } = StatusCodes.Status200OK;
    public object? Details { get; private init; }

    public bool Succeeded => Error is null;

    public static OpResult Ok(AuditLog log, object? data = null) => new() { Log = log, Data = data };

    public static OpResult Fail(string message, int status = StatusCodes.Status400BadRequest, object? details = null) =>
        new() { Error = message, Status = status, Details = details };

    public static OpResult NotFound(string message) => Fail(message, StatusCodes.Status404NotFound);
}
