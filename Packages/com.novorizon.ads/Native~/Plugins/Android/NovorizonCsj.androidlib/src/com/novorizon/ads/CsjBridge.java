package com.novorizon.ads;

import android.app.Activity;
import android.content.res.Configuration;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import com.bytedance.sdk.openadsdk.*;
import com.unity3d.player.UnityPlayer;
import java.util.HashMap;
import java.util.Map;
import org.json.JSONObject;

/** Public domestic CSJ 7.8 API. Direct CSJ inventory; other GroMore networks require their own adapters. */
public final class CsjBridge
{
    private static final Map<String, Entry> entries = new HashMap<>();
    private static final Handler main = new Handler(Looper.getMainLooper());
    private static String initializedAppId;

    private static final class Entry
    {
        String receiver, request;
        TTRewardVideoAd rewarded;
        TTFullScreenVideoAd fullscreen;
        boolean loaded, failed, videoError, shown, released;
    }

    public static void initialize(String receiver, String request, String appId, int format, String userId)
    {
        Activity activity = UnityPlayer.currentActivity;
        activity.runOnUiThread(() -> {
            if (initializedAppId != null && !initializedAppId.equals(appId))
            {
                send(receiver, request, "init_failed", "", 0);
                return;
            }
            if (TTAdSdk.isSdkReady())
            {
                // Do not silently adopt a global SDK configured by another integration.
                send(receiver, request, appId.equals(initializedAppId) ? "initialized" : "init_failed", "", 0);
                return;
            }
            try
            {
                TTCustomController privacy = new TTCustomController() {
                    @Override public boolean isCanUseLocation() { return false; }
                    @Override public boolean isCanUsePhoneState() { return false; }
                    @Override public boolean isCanUseWifiState() { return false; }
                    @Override public boolean isCanUseWriteExternal() { return false; }
                    @Override public boolean isCanUseAndroidId() { return false; }
                    @Override public boolean isCanUsePermissionRecordAudio() { return false; }
                    @Override public boolean alist() { return false; }
                    @Override public String getDevOaid() { return ""; }
                    @Override public Map<String, Object> userPrivacyConfig()
                    {
                        Map<String, Object> values = new HashMap<>();
                        values.put("motion_info", "0");
                        return values;
                    }
                };
                TTAdConfig config = new TTAdConfig.Builder().appId(appId).appName(activity.getApplicationInfo().loadLabel(activity.getPackageManager()).toString())
                    .debug(false).useMediation(false).customController(privacy).build();
                if (!TTAdSdk.init(activity.getApplicationContext(), config))
                {
                    send(receiver, request, "init_failed", "", 0);
                    return;
                }
                initializedAppId = appId;
                TTAdSdk.start(new TTAdSdk.Callback() {
                    @Override public void success() { send(receiver, request, "initialized", "", 0); }
                    @Override public void fail(int code, String message) { send(receiver, request, "init_failed", "", 0); }
                });
            }
            catch (Exception ignored)
            {
                send(receiver, request, "init_failed", "", 0);
            }
        });
    }

    public static void load(String receiver, String request, String unit, int format, String userId)
    {
        Activity activity = UnityPlayer.currentActivity;
        activity.runOnUiThread(() -> {
            Entry entry = new Entry();
            entry.receiver = receiver;
            entry.request = request;
            entries.put(request, entry);
            try
            {
                int orientation = activity.getResources().getConfiguration().orientation == Configuration.ORIENTATION_LANDSCAPE
                    ? TTAdConstant.HORIZONTAL : TTAdConstant.VERTICAL;
                AdSlot slot = new AdSlot.Builder().setCodeId(unit).setSupportDeepLink(true).setOrientation(orientation)
                    .setUserID(userId).setMediaExtra(request).build();
                TTAdNative loader = TTAdSdk.getAdManager().createAdNative(activity);
                if (format == 0)
                {
                    loader.loadRewardVideoAd(slot, new TTAdNative.RewardVideoAdListener() {
                        @Override public void onError(int code, String message) { failLoad(entry); }
                        @Override public void onRewardVideoAdLoad(TTRewardVideoAd ad) { main.post(() -> entry.rewarded = ad); }
                        @Override public void onRewardVideoCached() { cached(entry); }
                        @Override public void onRewardVideoCached(TTRewardVideoAd ad)
                        {
                            main.post(() -> { entry.rewarded = ad; cached(entry); });
                        }
                    });
                }
                else
                {
                    loader.loadFullScreenVideoAd(slot, new TTAdNative.FullScreenVideoAdListener() {
                        @Override public void onError(int code, String message) { failLoad(entry); }
                        @Override public void onFullScreenVideoAdLoad(TTFullScreenVideoAd ad) { main.post(() -> entry.fullscreen = ad); }
                        @Override public void onFullScreenVideoCached() { cached(entry); }
                        @Override public void onFullScreenVideoCached(TTFullScreenVideoAd ad)
                        {
                            main.post(() -> { entry.fullscreen = ad; cached(entry); });
                        }
                    });
                }
            }
            catch (Exception ignored)
            {
                failLoad(entry);
            }
        });
    }

    private static void cached(Entry entry)
    {
        main.post(() -> {
            if (entries.get(entry.request) == entry && !entry.failed && !entry.loaded && !entry.shown && !entry.released &&
                (entry.rewarded != null || entry.fullscreen != null))
            {
                entry.loaded = true;
                send(entry, "loaded", "", 0);
            }
        });
    }

    private static void failLoad(Entry entry)
    {
        main.post(() -> {
            if (!entry.loaded && !entry.failed)
            {
                entry.failed = true;
                entries.remove(entry.request);
                send(entry, "load_failed", "", 0);
            }
        });
    }

    public static void show(String receiver, String request, String value, int format, String userId)
    {
        Activity activity = UnityPlayer.currentActivity;
        activity.runOnUiThread(() -> {
            Entry entry = entries.get(request);
            if (entry == null || !entry.loaded)
            {
                send(receiver, request, "display_failed", "", 0);
                return;
            }
            entry.loaded = false;
            entry.shown = true;
            try
            {
                if (format == 0 && entry.rewarded != null)
                {
                    entry.rewarded.setRewardAdInteractionListener(new TTRewardVideoAd.RewardAdInteractionListener() {
                        @Override public void onAdShow() { }
                        @Override public void onAdVideoBarClick() { }
                        @Override public void onAdClose() { close(entry); }
                        @Override public void onVideoComplete() { }
                        @Override public void onVideoError() { main.post(() -> entry.videoError = true); }
                        @Override public void onSkippedVideo() { }
                        @Override public void onRewardVerify(boolean valid, int amount, String name, int code, String message) { }
                        @Override public void onRewardArrived(boolean valid, int type, Bundle extra)
                        {
                            if (valid && type == TTRewardVideoAd.REWARD_TYPE_DEFAULT && extra != null)
                            {
                                Object amount = extra.get(TTRewardVideoAd.REWARD_EXTRA_KEY_REWARD_AMOUNT);
                                if (amount instanceof Number)
                                {
                                    send(entry, "reward", extra.getString(TTRewardVideoAd.REWARD_EXTRA_KEY_REWARD_NAME, ""),
                                        ((Number) amount).doubleValue());
                                }
                            }
                        }
                    });
                    entry.rewarded.showRewardVideoAd(activity);
                }
                else if (format == 1 && entry.fullscreen != null)
                {
                    entry.fullscreen.setFullScreenVideoAdInteractionListener(new TTFullScreenVideoAd.FullScreenVideoAdInteractionListener() {
                        @Override public void onAdShow() { }
                        @Override public void onAdVideoBarClick() { }
                        @Override public void onAdClose() { close(entry); }
                        @Override public void onVideoComplete() { }
                        @Override public void onSkippedVideo() { }
                    });
                    entry.fullscreen.showFullScreenVideoAd(activity);
                }
                else
                {
                    send(receiver, request, "display_failed", "", 0);
                    entries.remove(request);
                }
            }
            catch (Exception ignored)
            {
                send(receiver, request, "display_failed", "", 0);
                entries.remove(request);
            }
        });
    }

    private static void close(Entry entry)
    {
        main.post(() -> {
            send(entry, entry.videoError ? "display_failed" : "closed", "", 0);
            // Retain per-ad delegates briefly for a late reward callback, keyed by this ad instance.
            main.postDelayed(() -> entries.remove(entry.request), 120000);
        });
    }

    public static void release(String receiver, String request, String value, int format, String userId)
    {
        main.post(() -> {
            Entry entry = entries.remove(request);
            if (entry != null)
            {
                entry.released = true;
            }
        });
    }

    private static void send(Entry entry, String kind, String label, double amount)
    {
        main.post(() -> {
            if (!entry.released)
            {
                send(entry.receiver, entry.request, kind, label, amount);
            }
        });
    }

    private static void send(String receiver, String request, String kind, String label, double amount)
    {
        main.post(() -> {
            try
            {
                JSONObject result = new JSONObject();
                result.put("requestId", request);
                result.put("kind", kind);
                result.put("label", label == null ? "" : label);
                result.put("amount", amount);
                UnityPlayer.UnitySendMessage(receiver, "OnNovorizonCsjResult", result.toString());
            }
            catch (Exception ignored) { }
        });
    }
}
