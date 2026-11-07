#import <BUAdSDK/BUAdSDK.h>
#import "UnityInterface.h"

static NSMutableDictionary *NVAds;
static NSString *NVAdAppId;

static NSString *NVAdString(const char *value)
{
    return value ? [NSString stringWithUTF8String:value] : @"";
}

static void NVAdSend(NSString *receiver, NSString *request, NSString *kind, NSString *label, double amount)
{
    dispatch_async(dispatch_get_main_queue(), ^{
        NSDictionary *body = @{@"requestId": request, @"kind": kind, @"label": label ?: @"", @"amount": @(amount)};
        NSData *data = [NSJSONSerialization dataWithJSONObject:body options:0 error:nil];
        NSString *json = [[NSString alloc] initWithData:data encoding:NSUTF8StringEncoding];
        UnitySendMessage(receiver.UTF8String, "OnNovorizonCsjResult", json.UTF8String);
    });
}

@interface NVAdPrivacy : NSObject <BUAdSDKPrivacyProvider>
@end
@implementation NVAdPrivacy
- (BOOL)canUseLocation { return NO; }
- (BOOL)canUseWiFiBSSID { return NO; }
- (NSDictionary *)privacyConfig { return @{@"motion_info": @"0"}; }
@end

@interface NVAdEntry : NSObject <BUNativeExpressRewardedVideoAdDelegate, BUNativeExpressFullscreenVideoAdDelegate>
@property(nonatomic, copy) NSString *receiver;
@property(nonatomic, copy) NSString *request;
@property(nonatomic, strong) BUNativeExpressRewardedVideoAd *rewarded;
@property(nonatomic, strong) BUNativeExpressFullscreenVideoAd *fullscreen;
@property(nonatomic) BOOL loaded;
@property(nonatomic) BOOL failed;
@property(nonatomic) BOOL videoError;
@property(nonatomic) BOOL released;
@end

@implementation NVAdEntry
- (void)send:(NSString *)kind label:(NSString *)label amount:(double)amount
{
    dispatch_async(dispatch_get_main_queue(), ^{
        if (!self.released)
        {
            NVAdSend(self.receiver, self.request, kind, label, amount);
        }
    });
}
- (void)cached
{
    dispatch_async(dispatch_get_main_queue(), ^{
        if (!self.loaded && !self.failed && !self.released)
        {
            self.loaded = YES;
            [self send:@"loaded" label:nil amount:0];
        }
    });
}
- (void)loadFailed
{
    dispatch_async(dispatch_get_main_queue(), ^{
        if (!self.loaded && !self.failed && !self.released)
        {
            self.failed = YES;
            [self send:@"load_failed" label:nil amount:0];
        }
    });
}
- (void)closed
{
    [self send:self.videoError ? @"display_failed" : @"closed" label:nil amount:0];
    NSString *identifier = self.request;
    dispatch_after(dispatch_time(DISPATCH_TIME_NOW, 120 * NSEC_PER_SEC), dispatch_get_main_queue(), ^{
        [NVAds removeObjectForKey:identifier];
    });
}
- (void)nativeExpressRewardedVideoAdDidDownLoadVideo:(BUNativeExpressRewardedVideoAd *)ad { [self cached]; }
- (void)nativeExpressRewardedVideoAd:(BUNativeExpressRewardedVideoAd *)ad didFailWithError:(NSError *)error { [self loadFailed]; }
- (void)nativeExpressRewardedVideoAdViewRenderFail:(BUNativeExpressRewardedVideoAd *)ad error:(NSError *)error
{
    [self send:@"display_failed" label:nil amount:0];
}
- (void)nativeExpressRewardedVideoAdDidPlayFinish:(BUNativeExpressRewardedVideoAd *)ad didFailWithError:(NSError *)error
{
    self.videoError = error != nil;
}
- (void)nativeExpressRewardedVideoAdDidClose:(BUNativeExpressRewardedVideoAd *)ad { [self closed]; }
- (void)nativeExpressRewardedVideoAdServerRewardDidSucceed:(BUNativeExpressRewardedVideoAd *)ad verify:(BOOL)verify
{
    if (verify && ad.rewardedVideoModel.rewardType == BURewardType_Default)
    {
        [self send:@"reward" label:ad.rewardedVideoModel.rewardName amount:ad.rewardedVideoModel.rewardAmount];
    }
}
- (void)nativeExpressFullscreenVideoAdDidDownLoadVideo:(BUNativeExpressFullscreenVideoAd *)ad { [self cached]; }
- (void)nativeExpressFullscreenVideoAd:(BUNativeExpressFullscreenVideoAd *)ad didFailWithError:(NSError *)error { [self loadFailed]; }
- (void)nativeExpressFullscreenVideoAdViewRenderFail:(BUNativeExpressFullscreenVideoAd *)ad error:(NSError *)error
{
    [self send:@"display_failed" label:nil amount:0];
}
- (void)nativeExpressFullscreenVideoAdDidPlayFinish:(BUNativeExpressFullscreenVideoAd *)ad didFailWithError:(NSError *)error
{
    self.videoError = error != nil;
}
- (void)nativeExpressFullscreenVideoAdDidClose:(BUNativeExpressFullscreenVideoAd *)ad { [self closed]; }
@end

extern "C" void NovorizonCsj(const char *receiver, const char *operation, const char *request,
                             const char *value, int format, const char *userId)
{
    NSString *target = NVAdString(receiver), *action = NVAdString(operation), *identifier = NVAdString(request);
    NSString *parameter = NVAdString(value), *user = NVAdString(userId);
    dispatch_async(dispatch_get_main_queue(), ^{
        if (!NVAds) { NVAds = [NSMutableDictionary new]; }
        if ([action isEqualToString:@"initialize"])
        {
            if ((NVAdAppId && ![NVAdAppId isEqualToString:parameter]) ||
                (BUAdSDKManager.state == BUAdSDKStateStart && !NVAdAppId))
            {
                NVAdSend(target, identifier, @"init_failed", nil, 0);
                return;
            }
            NVAdAppId = parameter;
            BUAdSDKConfiguration *configuration = BUAdSDKConfiguration.configuration;
            configuration.appID = parameter;
            configuration.unityDeveloper = YES;
            configuration.SDKDEBUG = NO;
            configuration.debugLog = @0;
            configuration.privacyProvider = [NVAdPrivacy new];
            [BUAdSDKManager startWithAsyncCompletionHandler:^(BOOL success, NSError *error) {
                NVAdSend(target, identifier, success ? @"initialized" : @"init_failed", nil, 0);
            }];
        }
        else if ([action isEqualToString:@"load"])
        {
            NVAdEntry *entry = [NVAdEntry new];
            entry.receiver = target;
            entry.request = identifier;
            NVAds[identifier] = entry;
            if (format == 0)
            {
                BURewardedVideoModel *model = [BURewardedVideoModel new];
                model.userId = user;
                model.extra = identifier;
                entry.rewarded = [[BUNativeExpressRewardedVideoAd alloc] initWithSlotID:parameter rewardedVideoModel:model];
                entry.rewarded.delegate = entry;
                [entry.rewarded loadAdData];
            }
            else
            {
                entry.fullscreen = [[BUNativeExpressFullscreenVideoAd alloc] initWithSlotID:parameter];
                entry.fullscreen.delegate = entry;
                [entry.fullscreen loadAdData];
            }
        }
        else if ([action isEqualToString:@"show"])
        {
            NVAdEntry *entry = NVAds[identifier];
            BOOL success = NO;
            if (entry.loaded && !entry.released)
            {
                entry.loaded = NO;
                success = format == 0 ? [entry.rewarded showAdFromRootViewController:UnityGetGLViewController()] :
                                       [entry.fullscreen showAdFromRootViewController:UnityGetGLViewController()];
            }
            if (!success)
            {
                NVAdSend(target, identifier, @"display_failed", nil, 0);
                [NVAds removeObjectForKey:identifier];
            }
        }
        else if ([action isEqualToString:@"release"])
        {
            NVAdEntry *entry = NVAds[identifier];
            entry.released = YES;
            entry.rewarded.delegate = nil;
            entry.fullscreen.delegate = nil;
            [NVAds removeObjectForKey:identifier];
        }
    });
}
