namespace DukkanPaneli.Api.Common;

/// <summary>Servis sonuçlarını HTTP yanıtına çeviren yardımcılar. Yanıt biçimi arayüzle (core.js → api()) ortaktır.</summary>
public static class HttpResults
{
    /// <summary>Hata yanıtı: <c>{ error, details }</c>.</summary>
    public static IResult Fail(string message, int status = StatusCodes.Status400BadRequest, object? details = null) =>
        Results.Json(new { error = message, details }, statusCode: status);

    /// <summary>
    /// Başarılı değişiklik yanıtı: <c>{ auditId, undoable, summary, data }</c>.
    /// Arayüz auditId ile bildirimdeki "Geri al" düğmesini çalıştırır.
    /// </summary>
    public static IResult ToHttp(this OpResult r) => r.Succeeded
        ? Results.Ok(new { auditId = r.Log!.Id, undoable = r.Log.Undoable, summary = r.Log.Summary, data = r.Data })
        : Fail(r.Error!, r.Status, r.Details);

    /// <summary>Sorgu sonucu: bulunduysa 200, bulunamadıysa 404.</summary>
    public static IResult OkOrNotFound<T>(T? value, string notFoundMessage) where T : class =>
        value is null ? Fail(notFoundMessage, StatusCodes.Status404NotFound) : Results.Ok(value);
}
