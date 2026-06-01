using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

[assembly: AssemblyTitle("Utils")]
[assembly: AssemblyProduct("Utils")]
[assembly: AssemblyVersion("0.9.7.0")]
[assembly: AssemblyFileVersion("0.9.7.0")]
[assembly: ComVisible(false)]

namespace Utils
{
    public enum LogLevel
    {
        DEBUG,
        INFO,
        WARNING,
        ERROR
    }

    public sealed class LogEventArgs : EventArgs
    {
        public LogEventArgs(string message, LogLevel logLevel)
        {
            Message = message;
            LogLevel = logLevel;
        }

        public string Message { get; }

        public LogLevel LogLevel { get; }
    }

    public interface ILoggable
    {
        event EventHandler<LogEventArgs> OnLogEntry;

        void RaiseLogEntry(LogEventArgs e);
    }

    public interface IPersistent
    {
        void Save(string path);

        void Load(string path);
    }
}

namespace Phinix.Framework
{
    public enum FrameworkFlow
    {
        Unspecified = 0,
        Message = 1,
        Command = 2,
        Item = 3
    }

    public enum FrameworkCommandKind
    {
        Unspecified = 0,
        Request = 1,
        Response = 2,
        Event = 3,
        State = 4
    }
}

namespace Utils.Framework
{
    public static class FrameworkProtocol
    {
        public const int Version = 2;
        public const string KindCommand = "command";
        public const string SystemSenderUuid = "__phinix_system__";
    }

    public enum FrameworkCompatibilityMode
    {
        Unknown = 0,
        FrameworkV2 = 1,
        Legacy = 2
    }

    public enum MessageHandlingResultAction
    {
        Continue = 0,
        Handled = 1,
        Handle = 1,
        ReplacePayload = 2,
        Replace = 2,
        SuppressDefault = 3,
        StopPropagation = 4,
        Block = 4,
        LegacyFallback = 5,
        Observe = 6
    }

    public interface IPhinixExtension
    {
        string ExtensionId { get; }
    }

    public interface IPhinixExtensionModule : IPhinixExtension
    {
        void Register(IExtensionBuilder builder);
    }

    public interface IActivatablePhinixExtensionModule : IPhinixExtension
    {
        void Activate(ExtensionHostContext hostContext);

        void Shutdown(ExtensionHostContext hostContext);
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class PhinixExtensionAttribute : Attribute
    {
        public PhinixExtensionAttribute(string extensionId)
        {
            ExtensionId = extensionId;
        }

        public string ExtensionId { get; }
    }

    public interface IExtensionApiRegistry
    {
        void RegisterApi<T>(string extensionId, T implementation) where T : class;

        bool TryResolve<T>(out T implementation) where T : class;

        IReadOnlyList<T> ResolveAll<T>() where T : class;
    }

    public interface IExtensionBuilder
    {
        string ExtensionId { get; }

        ExtensionHostContext HostContext { get; }

        IExtensionApiRegistry ApiRegistry { get; }

        void AddCapabilityProvider(ICapabilityProvider capabilityProvider);

        void AddMessageInterceptor(IMessageInterceptor interceptor);

        void AddMessageRenderer(IMessageRenderer renderer);

        void AddClientMessageHandler(IClientMessageHandler handler);

        void AddServerMessageHandler(IServerMessageHandler handler);

        void AddServerInboundMessageInterceptor(IServerInboundMessageInterceptor interceptor);

        void AddServerDefaultMessageHandler(IServerDefaultMessageHandler handler);

        void AddServerMessageObserver(IServerMessageObserver observer);

        void AddItemCodec(IItemCodec codec);

        void AddClientCommandHandler(IClientCommandHandler handler);

        void AddServerCommandHandler(IServerCommandHandler handler);

        void AddServerInboundCommandInterceptor(IServerInboundCommandInterceptor interceptor);

        void AddServerDefaultCommandHandler(IServerDefaultCommandHandler handler);

        void AddServerCommandObserver(IServerCommandObserver observer);

        void AddServerOutboundPacketInterceptor(IServerOutboundPacketInterceptor interceptor);

        void RegisterApi<T>(T implementation) where T : class;

        bool TryResolveApi<T>(out T implementation) where T : class;

        IReadOnlyList<T> ResolveApis<T>() where T : class;
    }

    public sealed class ExtensionHostContext
    {
        private readonly Dictionary<Type, object> services = new Dictionary<Type, object>();

        public string HostKind { get; set; }

        public Action<string, Utils.LogLevel> Log { get; set; }

        public Func<DateTime> UtcNow { get; set; } = () => DateTime.UtcNow;

        public IExtensionApiRegistry ApiRegistry { get; internal set; }

        public void AddService<T>(T service) where T : class
        {
            services[typeof(T)] = service;
        }

        public bool TryGetService<T>(out T service) where T : class
        {
            if (services.TryGetValue(typeof(T), out object value) && value is T typed)
            {
                service = typed;
                return true;
            }

            service = null;
            return false;
        }

        public T GetRequiredService<T>() where T : class
        {
            if (TryGetService(out T service))
            {
                return service;
            }

            throw new InvalidOperationException(typeof(T).FullName);
        }

        public void RegisterPersistent(string extensionId, string logicalName, Utils.IPersistent persistent)
        {
        }
    }

    public interface ICapabilityProvider
    {
        IEnumerable<string> GetCapabilities();
    }

    public interface IMessageHandler
    {
        int Priority { get; }
    }

    public interface IMessageInterceptor
    {
        int Priority { get; }

        MessageHandlingResultAction Intercept(FrameworkDisplayMessage message);
    }

    public interface IMessageRenderer
    {
        bool CanRender(FrameworkPacket message);

        FrameworkDisplayMessage Render(FrameworkPacket message);
    }

    public interface IClientMessageHandler : IMessageHandler
    {
        bool CanHandleOutgoingText(string rawMessage);

        ClientOutgoingMessageResult HandleOutgoingText(string rawMessage, ClientFrameworkContext context);

        bool CanHandleIncomingMessage(FrameworkPacket message);

        ClientIncomingMessageResult HandleIncomingMessage(FrameworkPacket message, ClientFrameworkContext context);
    }

    public interface IServerMessageHandler : IMessageHandler
    {
        bool CanHandleIncomingMessage(FrameworkPacket message);

        ServerIncomingMessageResult HandleIncomingMessage(FrameworkPacket message, ServerFrameworkContext context);
    }

    public interface IServerInboundMessageInterceptor : IMessageHandler
    {
        bool CanInterceptIncomingMessage(FrameworkPacket message);

        ServerIncomingMessageResult InterceptIncomingMessage(FrameworkPacket message, ServerFrameworkContext context);
    }

    public interface IServerDefaultMessageHandler : IServerMessageHandler
    {
    }

    public interface IServerMessageObserver : IMessageHandler
    {
        bool CanObserveIncomingMessage(FrameworkPacket message);

        void ObserveIncomingMessage(FrameworkPacket message, ServerFrameworkContext context, MessageHandlingResultAction terminalAction);
    }

    public interface ICommandHandler
    {
        int Priority { get; }
    }

    public interface IClientCommandHandler : ICommandHandler
    {
        bool CanHandleIncomingCommand(FrameworkPacket command);

        ClientIncomingCommandResult HandleIncomingCommand(FrameworkPacket command, ClientFrameworkContext context);
    }

    public interface IClientOutgoingCommandHandler : ICommandHandler
    {
        bool CanHandleOutgoingCommand(FrameworkPacket command);

        ClientOutgoingCommandResult HandleOutgoingCommand(FrameworkPacket command, ClientFrameworkContext context);
    }

    public interface IServerCommandHandler : ICommandHandler
    {
        bool CanHandleIncomingCommand(FrameworkPacket command);

        ServerIncomingCommandResult HandleIncomingCommand(FrameworkPacket command, ServerFrameworkContext context);
    }

    public interface IServerInboundCommandInterceptor : ICommandHandler
    {
        bool CanInterceptIncomingCommand(FrameworkPacket command);

        ServerIncomingCommandResult InterceptIncomingCommand(FrameworkPacket command, ServerFrameworkContext context);
    }

    public interface IServerDefaultCommandHandler : IServerCommandHandler
    {
    }

    public interface IServerCommandObserver : ICommandHandler
    {
        bool CanObserveIncomingCommand(FrameworkPacket command);

        void ObserveIncomingCommand(FrameworkPacket command, ServerFrameworkContext context, MessageHandlingResultAction terminalAction);
    }

    public interface IServerOutboundPacketInterceptor
    {
        int Priority { get; }

        bool CanInterceptOutgoingPacket(FrameworkPacket packet, ServerOutboundPacketContext context);

        ServerOutgoingPacketResult InterceptOutgoingPacket(FrameworkPacket packet, ServerOutboundPacketContext context);
    }

    public interface IItemCodec
    {
        string CodecId { get; }

        bool CanEncode(object item, ItemCodecContext context);

        FrameworkItemPayload Encode(object item, ItemCodecContext context);

        bool CanDecode(FrameworkItemPayload payload, ItemCodecContext context);

        object Decode(FrameworkItemPayload payload, ItemCodecContext context);
    }

    public interface IFrameworkServerPacketDispatcher
    {
        void Send(string connectionId, FrameworkPacket packet);

        void Send(string connectionId, FrameworkPacket packet, string sourceExtensionId);
    }

    public sealed class ClientOutgoingMessageResult
    {
        public MessageHandlingResultAction Action { get; set; } = MessageHandlingResultAction.Handled;

        public FrameworkPacket Message { get; set; }
    }

    public sealed class ClientIncomingMessageResult
    {
        public MessageHandlingResultAction Action { get; set; } = MessageHandlingResultAction.Handled;

        public FrameworkPacket Message { get; set; }

        public FrameworkDisplayMessage DisplayMessage { get; set; }
    }

    public sealed class ServerIncomingMessageResult
    {
        public MessageHandlingResultAction Action { get; set; } = MessageHandlingResultAction.Handled;

        public FrameworkPacket Message { get; set; }
    }

    public sealed class ClientIncomingCommandResult
    {
        public MessageHandlingResultAction Action { get; set; } = MessageHandlingResultAction.Handled;

        public FrameworkPacket Command { get; set; }

        public FrameworkDisplayMessage DisplayMessage { get; set; }
    }

    public sealed class ClientOutgoingCommandResult
    {
        public MessageHandlingResultAction Action { get; set; } = MessageHandlingResultAction.Handled;

        public FrameworkPacket Command { get; set; }
    }

    public sealed class ServerIncomingCommandResult
    {
        public MessageHandlingResultAction Action { get; set; } = MessageHandlingResultAction.Handled;

        public FrameworkPacket Command { get; set; }
    }

    public sealed class ServerOutgoingPacketResult
    {
        public MessageHandlingResultAction Action { get; set; } = MessageHandlingResultAction.Continue;

        public FrameworkPacket Packet { get; set; }

        public IReadOnlyCollection<string> TargetConnectionIds { get; set; }
    }

    public sealed class ClientFrameworkContext
    {
        public string SessionId { get; set; }

        public string SenderUuid { get; set; }

        public FrameworkCompatibilityMode CompatibilityMode { get; set; }

        public Action<FrameworkPacket> SendMessage { get; set; }

        public IReadOnlyCollection<string> RemoteCapabilities { get; set; }

        public Func<string, bool> HasRemoteCapability { get; set; }

        public Action<string, Utils.LogLevel> Log { get; set; }
    }

    public sealed class ServerFrameworkContext
    {
        public string ConnectionId { get; set; }

        public string SessionId { get; set; }

        public string SenderUuid { get; set; }

        public string SourceExtensionId { get; set; }

        public Action<string, FrameworkPacket> SendMessage { get; set; }

        public Action<FrameworkPacket, string[]> BroadcastMessage { get; set; }

        public Func<string, bool> IsConnectionFrameworkCapable { get; set; }

        public IReadOnlyCollection<string> RemoteCapabilities { get; set; }

        public IReadOnlyCollection<string> ServerCapabilities { get; set; }

        public Func<string, bool> HasRemoteCapability { get; set; }

        public Func<string, string, bool> ConnectionHasCapability { get; set; }

        public Action<string, Utils.LogLevel> Log { get; set; }
    }

    public sealed class ServerOutboundPacketContext
    {
        public string SourceExtensionId { get; set; }

        public IReadOnlyCollection<string> TargetConnectionIds { get; set; }

        public Action<string, FrameworkPacket> DeliverToConnection { get; set; }

        public Func<string, bool> IsConnectionFrameworkCapable { get; set; }

        public Func<string, string, bool> ConnectionHasCapability { get; set; }

        public Action<string, Utils.LogLevel> Log { get; set; }
    }

    public sealed class ItemCodecContext
    {
        public FrameworkCompatibilityMode CompatibilityMode { get; set; }

        public Action<string, Utils.LogLevel> Log { get; set; }
    }

    [DataContract]
    public sealed class FrameworkMetadataEntry
    {
        [DataMember(Order = 0)]
        public string Key { get; set; }

        [DataMember(Order = 1)]
        public string Value { get; set; }
    }

    [DataContract]
    public sealed class FrameworkPacket
    {
        [DataMember(Order = 0)]
        public int Version { get; set; } = FrameworkProtocol.Version;

        [DataMember(Order = 1)]
        public string Kind { get; set; }

        [DataMember(Order = 2)]
        public string MessageType { get; set; }

        [DataMember(Order = 3)]
        public string MessageId { get; set; } = Guid.NewGuid().ToString();

        [DataMember(Order = 4)]
        public string SessionId { get; set; }

        [DataMember(Order = 5)]
        public string SenderUuid { get; set; }

        [DataMember(Order = 6)]
        public long TimestampUtcTicks { get; set; } = DateTime.UtcNow.Ticks;

        [DataMember(Order = 7)]
        public string PayloadJson { get; set; }

        [DataMember(Order = 8)]
        public List<FrameworkMetadataEntry> Metadata { get; set; } = new List<FrameworkMetadataEntry>();

        [DataMember(Order = 9)]
        public global::Phinix.Framework.FrameworkFlow Flow { get; set; }

        [DataMember(Order = 10)]
        public global::Phinix.Framework.FrameworkCommandKind CommandKind { get; set; }

        [DataMember(Order = 11)]
        public byte[] PayloadBytes { get; set; } = Array.Empty<byte>();
    }

    [DataContract]
    public sealed class FrameworkDisplayMessage
    {
        [DataMember(Order = 0)]
        public string MessageId { get; set; }

        [DataMember(Order = 1)]
        public string SenderUuid { get; set; }

        [DataMember(Order = 2)]
        public string Text { get; set; }
    }

    [DataContract]
    public sealed class FrameworkItemPayload
    {
        [DataMember(Order = 0)]
        public string CodecId { get; set; }

        [DataMember(Order = 1)]
        public string PayloadJson { get; set; }

        [DataMember(Order = 2)]
        public List<FrameworkMetadataEntry> Metadata { get; set; } = new List<FrameworkMetadataEntry>();

        [DataMember(Order = 3)]
        public byte[] PayloadBytes { get; set; } = Array.Empty<byte>();
    }

    public static class FrameworkMetadataKeys
    {
        public const string CorrelationId = "correlation_id";
        public const string SnapshotVersion = "snapshot_version";
        public const string StateKind = "state_kind";
    }

    public static class FrameworkMetadataStateKinds
    {
        public const string Snapshot = "snapshot";
        public const string Delta = "delta";
        public const string Projection = "projection";
        public const string Event = "event";
    }

    public static class FrameworkMetadataHelpers
    {
        public static string GetCorrelationId(this FrameworkPacket packet)
        {
            return packet.TryGetMetadataValue(FrameworkMetadataKeys.CorrelationId, out string correlationId) && !string.IsNullOrWhiteSpace(correlationId)
                ? correlationId
                : packet?.MessageId;
        }

        public static void SetCorrelationId(this FrameworkPacket packet, string correlationId)
        {
            packet.SetMetadataValue(FrameworkMetadataKeys.CorrelationId, correlationId);
        }

        public static void SetSnapshotVersion(this FrameworkPacket packet, long snapshotVersion)
        {
            packet.SetMetadataValue(FrameworkMetadataKeys.SnapshotVersion, snapshotVersion.ToString(CultureInfo.InvariantCulture));
        }

        public static void SetStateKind(this FrameworkPacket packet, string stateKind)
        {
            packet.SetMetadataValue(FrameworkMetadataKeys.StateKind, stateKind);
        }

        public static bool TryGetMetadataValue(this FrameworkPacket packet, string key, out string value)
        {
            value = null;
            if (packet?.Metadata == null)
            {
                return false;
            }

            FrameworkMetadataEntry entry = packet.Metadata.Find(candidate => string.Equals(candidate?.Key, key, StringComparison.OrdinalIgnoreCase));
            if (entry == null)
            {
                return false;
            }

            value = entry.Value;
            return true;
        }

        public static void SetMetadataValue(this FrameworkPacket packet, string key, string value)
        {
            if (packet == null || string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            packet.Metadata = packet.Metadata ?? new List<FrameworkMetadataEntry>();
            FrameworkMetadataEntry entry = packet.Metadata.Find(candidate => string.Equals(candidate?.Key, key, StringComparison.OrdinalIgnoreCase));
            if (entry == null)
            {
                packet.Metadata.Add(new FrameworkMetadataEntry { Key = key, Value = value ?? string.Empty });
                return;
            }

            entry.Value = value ?? string.Empty;
        }
    }

    public static class FrameworkSerialization
    {
        public static string SerializePayload<T>(T payload)
        {
            return Encoding.UTF8.GetString(Serialize(payload));
        }

        public static T DeserializePayload<T>(string json)
        {
            return Deserialize<T>(Encoding.UTF8.GetBytes(json ?? string.Empty));
        }

        private static byte[] Serialize<T>(T value)
        {
            DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(T));
            using (MemoryStream stream = new MemoryStream())
            {
                serializer.WriteObject(stream, value);
                return stream.ToArray();
            }
        }

        private static T Deserialize<T>(byte[] data)
        {
            DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(T));
            using (MemoryStream stream = new MemoryStream(data))
            {
                return (T)serializer.ReadObject(stream);
            }
        }
    }
}
