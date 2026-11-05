namespace Game.Framework.Network.Samples
{
    // These numeric meanings belong to the sample server, not the network transport.
    public enum GameServerResultCode
    {
        Ok = 0, InvalidRequest = 1, NotAuthenticated = 2, NotFound = 3,
        ServerBusy = 4, RequestTimeout = 5, Conflict = 6, InternalError = 7,
        Unsupported = 8, SessionReplaced = 9, InvalidCredentials = 100
    }
}
