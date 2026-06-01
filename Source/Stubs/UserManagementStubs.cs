using System;
using System.Reflection;
using System.Runtime.InteropServices;

[assembly: AssemblyTitle("UserManagement")]
[assembly: AssemblyProduct("UserManagement")]
[assembly: AssemblyVersion("0.9.7.0")]
[assembly: AssemblyFileVersion("0.9.7.0")]
[assembly: ComVisible(false)]

namespace UserManagement
{
    public struct ImmutableUser
    {
        public ImmutableUser(string uuid, string displayName = "???", bool loggedIn = false, bool acceptingTrades = false)
        {
            Uuid = uuid;
            DisplayName = displayName;
            LoggedIn = loggedIn;
            AcceptingTrades = acceptingTrades;
        }

        public string Uuid { get; }

        public string DisplayName { get; }

        public bool LoggedIn { get; }

        public bool AcceptingTrades { get; }
    }

    public sealed class ServerLoginEventArgs : EventArgs
    {
        public string ConnectionId;

        public string Uuid;

        public ServerLoginEventArgs(string connectionId, string uuid)
        {
            ConnectionId = connectionId;
            Uuid = uuid;
        }
    }

    public sealed class UserDisplayNameChangedEventArgs : EventArgs
    {
        public string Uuid;

        public string OldDisplayName;

        public string NewDisplayName;

        public UserDisplayNameChangedEventArgs(string uuid, string oldDisplayName, string newDisplayName)
        {
            Uuid = uuid;
            OldDisplayName = oldDisplayName;
            NewDisplayName = newDisplayName;
        }
    }

    public interface IServerUserManager
    {
        event EventHandler<ServerLoginEventArgs> OnLogin;

        bool IsLoggedIn(string connectionId, string uuid);

        string[] GetConnections();

        bool TryGetConnection(string uuid, out string connectionId);

        bool TryGetDisplayName(string uuid, out string displayName);

        bool TryGetLoggedIn(string uuid, out bool loggedIn);

        bool TryGetAcceptingTrades(string uuid, out bool acceptingTrades);
    }
}
