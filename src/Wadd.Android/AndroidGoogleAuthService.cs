using System;
using System.Threading;
using System.Threading.Tasks;
using Android.Gms.Auth.Api.SignIn;
using Wadd.Core.Interfaces;

namespace Wadd.Android;

public class AndroidGoogleAuthService : INativeGoogleAuthService
{
    public bool IsSupported => true;

    public async Task<NativeAuthResult> SignInAsync(string googleClientId, CancellationToken cancellationToken = default)
    {
        var mainActivity = MainActivity.Instance;
        if (mainActivity == null)
        {
            return new NativeAuthResult
            {
                IsSuccess = false,
                ErrorMessage = "Android MainActivity context is unavailable."
            };
        }

        try
        {
            var driveScope = new global::Android.Gms.Common.Apis.Scope("https://www.googleapis.com/auth/drive.file");
            var gsoBuilder = new global::Android.Gms.Auth.Api.SignIn.GoogleSignInOptions.Builder(global::Android.Gms.Auth.Api.SignIn.GoogleSignInOptions.DefaultSignIn)
                .RequestEmail()
                .RequestScopes(driveScope);

            if (!string.IsNullOrWhiteSpace(googleClientId))
            {
                gsoBuilder.RequestIdToken(googleClientId);
                gsoBuilder.RequestServerAuthCode(googleClientId);
            }

            var gso = gsoBuilder.Build();
            var signInClient = global::Android.Gms.Auth.Api.SignIn.GoogleSignIn.GetClient(mainActivity, gso);

            var tcs = new TaskCompletionSource<NativeAuthResult>();
            mainActivity.PendingAuthTcs = tcs;

            var signInIntent = signInClient.SignInIntent;
            mainActivity.StartActivityForResult(signInIntent, MainActivity.RcSignIn);

            using var reg = cancellationToken.Register(() =>
            {
                tcs.TrySetCanceled();
            });

            return await tcs.Task;
        }
        catch (Exception ex)
        {
            return new NativeAuthResult
            {
                IsSuccess = false,
                ErrorMessage = ex.Message
            };
        }
    }
}
