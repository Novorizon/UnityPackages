#if UNITY_2022_3_OR_NEWER
using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Game.Framework.Network.Samples
{
    // An explicit, read-only connection dashboard. Merely importing/playing does not connect.
    public sealed class TcpNetworkDemo : MonoBehaviour
    {
        private string deviceId = "";
        private string accountJwt = "";
        private string status = "Configure Resources/NetworkSettings, enter test credentials, then Connect.";
        private TcpNetworkSample sample;
        private bool ownsSample;
        private CancellationTokenSource operation;
        private bool busy;
        private bool destroyed;

        private void OnGUI()
        {
            bool previousEnabled = GUI.enabled;
            GUILayout.BeginArea(new Rect(16, 16, 680, 460), "TCP Network Demo", GUI.skin.box);
            GUILayout.Label("No automatic connection or data writes. Use a development account.");
            GUILayout.Label("Endpoint / encryption: Resources/NetworkSettings.asset");
            GUILayout.Label("State: " + (NetworkManager.IsCreated ? NetworkManager.Instance.State.ToString() : "Not initialized"));
            GUI.enabled = previousEnabled && !busy;
            GUILayout.Label("Device ID (from the account flow)");
            deviceId = GUILayout.TextField(deviceId, 256);
            GUILayout.Label("Account JWT (runtime only; never saved to the scene)");
            accountJwt = GUILayout.PasswordField(accountJwt, '*', 8192);
            if (GUILayout.Button("Connect and authenticate")) StartOperation(ConnectAsync);
            bool connected = NetworkManager.IsCreated && NetworkManager.Instance.IsConnected;
            GUI.enabled = previousEnabled && !busy && connected;
            if (GUILayout.Button("Read progress")) StartOperation(ReadAsync);
            if (GUILayout.Button("Read five requests concurrently")) StartOperation(ReadConcurrentlyAsync);
            GUI.enabled = previousEnabled && busy;
            if (GUILayout.Button("Cancel this operation")) operation?.Cancel();
            GUI.enabled = previousEnabled && !busy && NetworkManager.IsCreated;
            if (GUILayout.Button("Disconnect shared account connection")) StartOperation(DisconnectAsync);
            GUI.enabled = previousEnabled;
            GUILayout.Label(status);
            GUILayout.Label("Closing this demo cancels its pending connection attempt. It never disposes NetworkManager; Disconnect affects the shared connection.");
            GUILayout.EndArea();
        }

        private TcpNetworkSample GetSample()
        {
            if (sample == null)
            {
                sample = GetComponent<TcpNetworkSample>();
                if (sample == null) { sample = gameObject.AddComponent<TcpNetworkSample>(); ownsSample = true; }
            }
            return sample;
        }

        private void StartOperation(Func<CancellationToken, Task> action)
        {
            if (busy || destroyed) return;
            busy = true;
            var cancellation = new CancellationTokenSource();
            operation = cancellation;
            _ = RunAsync(action, cancellation);
        }

        private async Task RunAsync(Func<CancellationToken, Task> action, CancellationTokenSource cancellation)
        {
            try { await action(cancellation.Token); }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            { if (!destroyed) status = "Cancelled: reads stop waiting; connection attempts disconnect. Already-sent requests are not rolled back."; }
            catch (Exception error)
            {
                if (!destroyed) status = error.GetType().Name + ": " + error.Message;
                Debug.LogException(error);
            }
            finally
            {
                if (ReferenceEquals(operation, cancellation)) operation = null;
                cancellation.Dispose();
                busy = false;
            }
        }

        private async Task ConnectAsync(CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(deviceId) || string.IsNullOrWhiteSpace(accountJwt))
                throw new InvalidOperationException("Enter Device ID and a test-account JWT.");
            status = "Connecting and authenticating...";
            await GetSample().ConnectAsync(deviceId, accountJwt, ct);
            if (!destroyed) status = "Authenticated. PlayerId: " + GetSample().PlayerId;
        }

        private async Task ReadAsync(CancellationToken ct)
        {
            var progress = await GetSample().ReadProgressAsync(ct);
            if (!destroyed) status = "Progress: " + progress.Progress + ", version: " + progress.Version;
        }

        private async Task ReadConcurrentlyAsync(CancellationToken ct)
        {
            var responses = await GetSample().ReadConcurrentlyAsync(ct);
            if (!destroyed) status = "Received " + responses.Length + " independently matched responses.";
        }

        private async Task DisconnectAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            await GetSample().DisconnectAsync();
            if (!destroyed) status = "Disconnected; automatic reconnect stopped. Other pages share this connection.";
        }

        private void OnDestroy()
        {
            if (destroyed) return;
            destroyed = true;
            accountJwt = "";
            operation?.Cancel(); // The operation runner owns disposal.
            if (ownsSample && sample != null) Destroy(sample);
            sample = null;
        }
    }
}
#endif
