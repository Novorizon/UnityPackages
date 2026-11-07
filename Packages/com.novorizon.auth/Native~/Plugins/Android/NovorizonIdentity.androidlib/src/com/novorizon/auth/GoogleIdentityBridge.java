package com.novorizon.auth;

import android.app.Activity;
import android.os.CancellationSignal;
import androidx.credentials.Credential;
import androidx.credentials.CredentialManager;
import androidx.credentials.CredentialManagerCallback;
import androidx.credentials.CustomCredential;
import androidx.credentials.GetCredentialRequest;
import androidx.credentials.GetCredentialResponse;
import androidx.credentials.exceptions.GetCredentialCancellationException;
import androidx.credentials.exceptions.GetCredentialException;
import com.google.android.libraries.identity.googleid.GetSignInWithGoogleOption;
import com.google.android.libraries.identity.googleid.GoogleIdTokenCredential;
import com.unity3d.player.UnityPlayer;
import org.json.JSONObject;

/** Credential Manager explicit Google sign-in button flow. No access token or secret is logged. */
public final class GoogleIdentityBridge
{
    private static boolean pending;

    public static void signIn(String receiver, String request, String webClientId, String nonce)
    {
        Activity activity = UnityPlayer.currentActivity;
        activity.runOnUiThread(() -> {
            if (pending)
            {
                send(receiver, request, null, "google_busy", false);
                return;
            }
            pending = true;
            try
            {
                GetSignInWithGoogleOption option = new GetSignInWithGoogleOption.Builder(webClientId).setNonce(nonce).build();
                GetCredentialRequest parameters = new GetCredentialRequest.Builder().addCredentialOption(option).build();
                CredentialManager.create(activity).getCredentialAsync(activity, parameters, new CancellationSignal(),
                    activity::runOnUiThread, new CredentialManagerCallback<GetCredentialResponse, GetCredentialException>() {
                        @Override public void onResult(GetCredentialResponse response)
                        {
                            pending = false;
                            try
                            {
                                Credential credential = response.getCredential();
                                if (!(credential instanceof CustomCredential) ||
                                    !GoogleIdTokenCredential.TYPE_GOOGLE_ID_TOKEN_CREDENTIAL.equals(credential.getType()))
                                {
                                    send(receiver, request, null, "google_wrong_credential", false);
                                    return;
                                }
                                String token = GoogleIdTokenCredential.createFrom(credential.getData()).getIdToken();
                                send(receiver, request, token, null, false);
                            }
                            catch (Exception ignored)
                            {
                                send(receiver, request, null, "google_invalid_credential", false);
                            }
                        }
                        @Override public void onError(GetCredentialException error)
                        {
                            pending = false;
                            boolean cancelled = error instanceof GetCredentialCancellationException;
                            send(receiver, request, null, cancelled ? null : "google_authorization_failed", cancelled);
                        }
                    });
            }
            catch (Exception ignored)
            {
                pending = false;
                send(receiver, request, null, "google_start_failed", false);
            }
        });
    }

    private static void send(String receiver, String request, String token, String error, boolean cancelled)
    {
        try
        {
            JSONObject result = new JSONObject();
            result.put("requestId", request);
            result.put("token", token == null ? "" : token);
            result.put("error", error == null ? "" : error);
            result.put("cancelled", cancelled);
            UnityPlayer.UnitySendMessage(receiver, "OnNativeIdentityResult", result.toString());
        }
        catch (Exception ignored)
        {
            // JSONObject only receives strings/booleans. Never print credentials in fallback paths.
        }
    }
}
