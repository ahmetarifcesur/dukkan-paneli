namespace DukkanPaneli.Api.Common;

/// <summary>
/// Geçmişe yazılmayan işlemler (giriş, kurulum) için sonuç: ya bir değer ya bir hata.
/// Geçmişe yazılan değişiklikler için <see cref="OpResult"/> kullanılır.
/// </summary>
public readonly record struct Result<T>(T? Value, string? Error, int Status = StatusCodes.Status400BadRequest)
{
    public bool Succeeded => Error is null;

    public static Result<T> Ok(T value) => new(value, null, StatusCodes.Status200OK);
    public static Result<T> Fail(string error, int status = StatusCodes.Status400BadRequest) => new(default, error, status);
}
