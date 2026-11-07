#import <AuthenticationServices/AuthenticationServices.h>
#import <GoogleSignIn/GoogleSignIn.h>
#import "UnityInterface.h"

static NSString *NVString(const char *value)
{
    return value ? [NSString stringWithUTF8String:value] : @"";
}

static void NVIdentityResult(NSString *receiver, NSString *request, NSString *token,
                             NSString *accessToken, NSString *error, BOOL cancelled)
{
    dispatch_async(dispatch_get_main_queue(), ^{
        NSDictionary *body = @{@"requestId": request ?: @"", @"token": token ?: @"",
                               @"accessToken": accessToken ?: @"", @"error": error ?: @"",
                               @"cancelled": @(cancelled)};
        NSData *data = [NSJSONSerialization dataWithJSONObject:body options:0 error:nil];
        NSString *json = [[NSString alloc] initWithData:data encoding:NSUTF8StringEncoding];
        UnitySendMessage(receiver.UTF8String, "OnNativeIdentityResult", json.UTF8String);
    });
}

@interface NVAppleIdentity : NSObject <ASAuthorizationControllerDelegate, ASAuthorizationControllerPresentationContextProviding>
@property(nonatomic, copy) NSString *receiver;
@property(nonatomic, copy) NSString *request;
@property(nonatomic, strong) ASAuthorizationController *controller;
@end

static NVAppleIdentity *NVApplePending;
static BOOL NVGooglePending;

@implementation NVAppleIdentity
- (ASPresentationAnchor)presentationAnchorForAuthorizationController:(ASAuthorizationController *)controller
{
    return UnityGetGLViewController().view.window;
}
- (void)authorizationController:(ASAuthorizationController *)controller didCompleteWithAuthorization:(ASAuthorization *)authorization
{
    ASAuthorizationAppleIDCredential *credential = (ASAuthorizationAppleIDCredential *)authorization.credential;
    NSString *token = [[NSString alloc] initWithData:credential.identityToken encoding:NSUTF8StringEncoding];
    NVIdentityResult(self.receiver, self.request, token, nil, token.length ? nil : @"apple_no_token", NO);
    self.controller = nil;
    NVApplePending = nil;
}
- (void)authorizationController:(ASAuthorizationController *)controller didCompleteWithError:(NSError *)error
{
    BOOL cancelled = [error.domain isEqualToString:ASAuthorizationErrorDomain] && error.code == ASAuthorizationErrorCanceled;
    NVIdentityResult(self.receiver, self.request, nil, nil, cancelled ? nil : @"apple_authorization_failed", cancelled);
    self.controller = nil;
    NVApplePending = nil;
}
@end

extern "C" void NovorizonAppleSignIn(const char *receiver, const char *request, const char *hashedNonce)
{
    NSString *target = NVString(receiver), *identifier = NVString(request), *nonce = NVString(hashedNonce);
    dispatch_async(dispatch_get_main_queue(), ^{
        if (@available(iOS 13.0, *))
        {
            if (NVApplePending)
            {
                NVIdentityResult(target, identifier, nil, nil, @"apple_busy", NO);
                return;
            }
            NVAppleIdentity *delegate = [NVAppleIdentity new];
            delegate.receiver = target;
            delegate.request = identifier;
            NVApplePending = delegate;
            ASAuthorizationAppleIDRequest *authorization = [[ASAuthorizationAppleIDProvider new] createRequest];
            authorization.nonce = nonce;
            delegate.controller = [[ASAuthorizationController alloc] initWithAuthorizationRequests:@[authorization]];
            delegate.controller.delegate = delegate;
            delegate.controller.presentationContextProvider = delegate;
            [delegate.controller performRequests];
        }
        else
        {
            NVIdentityResult(target, identifier, nil, nil, @"apple_requires_ios13", NO);
        }
    });
}

extern "C" void NovorizonGoogleSignIn(const char *receiver, const char *request, const char *iosClientId,
                                     const char *webClientId, const char *rawNonce)
{
    NSString *target = NVString(receiver), *identifier = NVString(request);
    NSString *client = NVString(iosClientId), *server = NVString(webClientId), *nonce = NVString(rawNonce);
    dispatch_async(dispatch_get_main_queue(), ^{
        if (NVGooglePending)
        {
            NVIdentityResult(target, identifier, nil, nil, @"google_busy", NO);
            return;
        }
        NVGooglePending = YES;
        GIDSignIn.sharedInstance.configuration = [[GIDConfiguration alloc] initWithClientID:client serverClientID:server];
        [GIDSignIn.sharedInstance signInWithPresentingViewController:UnityGetGLViewController()
                                                              hint:nil
                                                  additionalScopes:nil
                                                             nonce:nonce
                                                        completion:^(GIDSignInResult *result, NSError *error) {
            NVGooglePending = NO;
            BOOL cancelled = [error.domain isEqualToString:kGIDSignInErrorDomain] && error.code == kGIDSignInErrorCodeCanceled;
            NVIdentityResult(target, identifier, result.user.idToken.tokenString, result.user.accessToken.tokenString,
                             error && !cancelled ? @"google_authorization_failed" : nil, cancelled);
        }];
    });
}

extern "C" void NovorizonGoogleHandleUrl(const char *url)
{
    NSString *value = NVString(url);
    dispatch_async(dispatch_get_main_queue(), ^{
        [GIDSignIn.sharedInstance handleURL:[NSURL URLWithString:value]];
    });
}
