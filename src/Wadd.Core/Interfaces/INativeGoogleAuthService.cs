namespace Wadd.Core.Interfaces;

public class NativeAuthResult
{
    public bool IsSuccess { get; set; }
    public string? IdToken { get; set; }
    public string? AccessToken { get; set; }
    public string? Email { get; set; }
    public string? DisplayName { get; set; }
    public string? ErrorMessage { get; set; }
}

public interface INativeGoogleAuthService
{
    bool IsSupported { get; }
    Task<NativeAuthResult> SignInAsync(string googleClientId, CancellationToken cancellationToken = default);
    Task<NativeAuthResult> TrySilentSignInAsync(string googleClientId, CancellationToken cancellationToken = default);
}
