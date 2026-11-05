#if UNITY_2022_3_OR_NEWER
using UnityEngine;
namespace Game.Framework.Network.Unity
{
    [CreateAssetMenu(menuName = "Novorizon/Network Settings", fileName = "NetworkSettings")]
    public sealed class NetworkSettings : ScriptableObject
    {
        public string Host = "127.0.0.1";
        public int Port = 1236;
        public PacketEncryptionMode Encryption = PacketEncryptionMode.AesGcm;
        public string CommunicationKeyBase64 = "";
        public bool AutoReconnect = true;
        public bool AutomaticDispatch = true;
        public int HeartbeatIntervalMilliseconds = 15000;
        public int HeartbeatMessageId = 1;
        internal NetworkConfiguration CreateConfiguration() => new NetworkConfiguration
        {
            Tcp = new TcpClientOptions { Host = Host, Port = Port, Encryption = Encryption, KeyBase64 = CommunicationKeyBase64 },
            Client = new NetworkClientOptions
            {
                AutoReconnect = AutoReconnect, AutomaticDispatch = AutomaticDispatch,
                HeartbeatIntervalMilliseconds = HeartbeatIntervalMilliseconds, HeartbeatMessageId = HeartbeatMessageId
            }
        };
    }
}
#endif
