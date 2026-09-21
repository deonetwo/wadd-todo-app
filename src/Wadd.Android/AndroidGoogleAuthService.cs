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
            var driveScope = new global::Android.Gms.Common.Apis.Scope("https://www.googleapis.com/auth/drive.appdata");
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

    public async Task<NativeAuthResult> TrySilentSignInAsync(string googleClientId, CancellationToken cancellationToken = default)
    {
        var context = global::Android.App.Application.Context;
        if (context == null)
        {
            return new NativeAuthResult { IsSuccess = false, ErrorMessage = "Android Application context is unavailable." };
        }

        try
        {
            var lastAccount = global::Android.Gms.Auth.Api.SignIn.GoogleSignIn.GetLastSignedInAccount(context);
            if (lastAccount != null && !string.IsNullOrWhiteSpace(lastAccount.Email))
            {
                var acct = lastAccount.Account ?? new global::Android.Accounts.Account(lastAccount.Email, "com.google");
                var accessToken = await Task.Run(() =>
                {
                    try
                    {
                        return global::Android.Gms.Auth.GoogleAuthUtil.GetToken(context, acct, "oauth2:https://www.googleapis.com/auth/drive.file email profile");
                    }
                    catch (Exception ex)
                    {
                        Wadd.Core.Logging.AppLogger.LogWarning("AndroidGoogleAuthService", "Silent GoogleAuthUtil.GetToken failed", ex);
                        return null;
                    }
                }, cancellationToken);

                if (!string.IsNullOrWhiteSpace(accessToken))
                {
                    return new NativeAuthResult
                    {
                        IsSuccess = true,
                        IdToken = lastAccount.IdToken,
                        AccessToken = accessToken,
                        Email = lastAccount.Email,
                        DisplayName = lastAccount.DisplayName
                    };
                }
            }
        }
        catch (Exception ex)
        {
            Wadd.Core.Logging.AppLogger.LogWarning("AndroidGoogleAuthService", "TrySilentSignInAsync failed", ex);
        }

        return new NativeAuthResult { IsSuccess = false, ErrorMessage = "No active Google account session found for silent refresh." };
    }
}
