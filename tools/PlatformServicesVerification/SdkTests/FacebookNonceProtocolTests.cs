using System;
using System.Reflection;
using Facebook.Unity;
using Game.Framework.Auth;
using NUnit.Framework;
using IdentitySource = Game.Framework.Auth.Facebook.FacebookIdentityProvider;

public sealed class FacebookNonceProtocolTests
{
    [Test]
    public void LimitedLoginKeepsRawNonceForFirebaseAndChecksHashedSdkChallenge()
    {
        string nonce = IdentityNonce.Create();
        var token = Token(IdentityNonce.Sha256(nonce));
        using (var proof = Convert(nonce, token))
        {
            Assert.AreEqual(IdentityProvider.Facebook, proof.Provider);
            Assert.AreEqual(IdentityTokenKind.IdToken, proof.Kind);
            Assert.AreEqual(nonce, proof.RawNonce);
            Assert.AreEqual("protocol-fixture-token", proof.Token);
        }
        // Firebase's documented Limited Login protocol requires a SHA-256 challenge, not raw nonce.
        var error = Assert.Throws<TargetInvocationException>(() => Convert(nonce, Token(nonce)));
        Assert.IsInstanceOf<InvalidOperationException>(error.InnerException);
        error = Assert.Throws<TargetInvocationException>(() => Convert(nonce, Token(IdentityNonce.Sha256("another-request"))));
        Assert.IsInstanceOf<InvalidOperationException>(error.InnerException);
    }

    private static IdentityProof Convert(string nonce, AuthenticationToken token)
    {
        var method = typeof(IdentitySource).GetMethod("LimitedProof", BindingFlags.Static | BindingFlags.NonPublic);
        return (IdentityProof)method.Invoke(null, new object[] { nonce, token });
    }

    private static AuthenticationToken Token(string challenge)
    {
        // Instantiate the real SDK 19 token value type without starting Facebook or making a request.
        return (AuthenticationToken)Activator.CreateInstance(
            typeof(AuthenticationToken),
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            new object[] { "protocol-fixture-token", challenge },
            null);
    }
}
