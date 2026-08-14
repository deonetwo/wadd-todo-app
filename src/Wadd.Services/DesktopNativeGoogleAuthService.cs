using Wadd.Core.Interfaces;

namespace Wadd.Services;

public class DesktopNativeGoogleAuthService : INativeGoogleAuthService
{
    public bool IsSupported => false;

    public Task<NativeAuthResult> SignInAsync(string googleClientId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new NativeAuthResult
        {
            IsSuccess = false,
            ErrorMessage = "Native Google Sign-In is not supported on desktop platforms."
        });
    }
}
