using System;

namespace Game.Framework.Network.Samples
{
    public sealed class LoginCredentials
    {
        public LoginCredentials(string deviceId, string accessToken)
        {
            if (string.IsNullOrWhiteSpace(deviceId) || string.IsNullOrWhiteSpace(accessToken))
                throw new ArgumentException("Device ID and access token are required.");
            DeviceId = deviceId;
            AccessToken = accessToken;
        }

        public string DeviceId { get; }
        public string AccessToken { get; }
    }
}
