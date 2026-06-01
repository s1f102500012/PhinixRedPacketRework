using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using UserManagement;
using Utils.Framework;
using Verse;

[assembly: AssemblyTitle("ClientExtensionAbstractions")]
[assembly: AssemblyProduct("ClientExtensionAbstractions")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
[assembly: ComVisible(false)]

namespace PhinixClient
{
    public interface IMainTabProvider
    {
        string TabLabel { get; }

        float TabOrder { get; }

        void Draw(UnityEngine.Rect inRect);
    }

    public interface IBadgeProvider
    {
        string BadgeText { get; }
    }
}

namespace PhinixClient.Framework
{
    public interface IFrameworkClientTransport
    {
        bool HasRemoteCapability(string capability);

        void SendFrameworkPacket(FrameworkPacket packet);

        bool TryHandleOutgoingMessage(string rawMessage);
    }

    public interface IFrameworkClientCommandTransport
    {
        bool TryHandleOutgoingCommand(FrameworkPacket command);
    }

    public sealed class FrameworkCompatibilityModeChangedEventArgs : EventArgs
    {
        public FrameworkCompatibilityModeChangedEventArgs(FrameworkCompatibilityMode compatibilityMode)
        {
            CompatibilityMode = compatibilityMode;
        }

        public FrameworkCompatibilityMode CompatibilityMode { get; }
    }

    public interface IFrameworkClientLifecycle
    {
        FrameworkCompatibilityMode CompatibilityMode { get; }

        event EventHandler<FrameworkCompatibilityModeChangedEventArgs> CompatibilityModeChanged;
    }

    public interface IClientSessionContext
    {
        bool Authenticated { get; }

        bool LoggedIn { get; }

        string SessionId { get; }

        string Uuid { get; }
    }

    public interface IClientSettingsContext
    {
        T Get<T>(string key, T defaultValue = default(T));

        void Set<T>(string key, T value);

        IEnumerable<string> BlockedUsers { get; }

        bool CollapseBlockedUsers { get; set; }

        void BlockUser(string uuid);

        void UnBlockUser(string uuid);

        event Action<string, object> OnSettingChanged;
    }

    public interface IClientUserDirectory
    {
        string Uuid { get; }

        ImmutableUser[] GetUsers(bool loggedIn = false);

        bool TryGetUser(string uuid, out ImmutableUser user);
    }

    public sealed class UserBlockStateChangedEventArgs : EventArgs
    {
        public UserBlockStateChangedEventArgs(string uuid, bool isBlocked)
        {
            Uuid = uuid;
            IsBlocked = isBlocked;
        }

        public string Uuid { get; }

        public bool IsBlocked { get; }
    }

    public interface IClientUserEventStream
    {
        event EventHandler Disconnected;

        event EventHandler UsersChanged;

        event EventHandler<UserDisplayNameChangedEventArgs> UserDisplayNameChanged;

        event EventHandler<UserBlockStateChangedEventArgs> BlockedUsersChanged;
    }

    public interface IClientMainThreadDispatcher
    {
        void Enqueue(Action action);
    }

    public interface IClientWindowService
    {
        void Open(Window window);

        void OpenSettingsWindow();
    }

    public interface IClientSoundService
    {
        void Enqueue(SoundDef soundDef);
    }

    public interface IClientSettingsPanelProvider
    {
        string SectionId { get; }

        float Order { get; }

        void DrawSettings(Listing_Standard listing, IClientSettingsContext settings);

        bool IsVisible(IClientSettingsContext settings);
    }
}
