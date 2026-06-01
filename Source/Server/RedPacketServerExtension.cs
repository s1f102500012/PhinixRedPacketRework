using System;
using System.Collections.Generic;
using System.Linq;
using UserManagement;
using Utils;
using Utils.Framework;

namespace Natsuki.PhinixRedPacketRework.Server
{
    [PhinixExtension(RedPacketProtocol.Capability)]
    public sealed class RedPacketServerExtension :
        IPhinixExtensionModule,
        IActivatablePhinixExtensionModule,
        ICapabilityProvider,
        IServerDefaultCommandHandler,
        IPersistent
    {
        private const string StorageName = "redpacket-state.xml";

        private readonly RedPacketServerStore store = new RedPacketServerStore();
        private readonly Random random = new Random();
        private readonly object randomLock = new object();

        private IServerUserManager userManager;
        private IFrameworkServerPacketDispatcher packetDispatcher;
        private EventHandler<ServerLoginEventArgs> loginHandler;

        public string ExtensionId => RedPacketProtocol.Capability;

        public int Priority => 1200;

        public void Register(IExtensionBuilder builder)
        {
            userManager = builder.HostContext.GetRequiredService<IServerUserManager>();
            packetDispatcher = builder.HostContext.GetRequiredService<IFrameworkServerPacketDispatcher>();
            builder.HostContext.RegisterPersistent(ExtensionId, StorageName, this);
            builder.AddCapabilityProvider(this);
            builder.AddServerDefaultCommandHandler(this);
        }

        public void Activate(ExtensionHostContext hostContext)
        {
            if (loginHandler == null)
            {
                loginHandler = (_, args) => SendSnapshotToConnection(args.ConnectionId, null, null);
            }

            if (userManager != null)
            {
                userManager.OnLogin -= loginHandler;
                userManager.OnLogin += loginHandler;
            }
        }

        public void Shutdown(ExtensionHostContext hostContext)
        {
            if (userManager != null && loginHandler != null)
            {
                userManager.OnLogin -= loginHandler;
            }
        }

        public IEnumerable<string> GetCapabilities()
        {
            yield return RedPacketProtocol.Capability;
            yield return RedPacketProtocol.SnapshotType;
            yield return RedPacketProtocol.CreateRequestType;
            yield return RedPacketProtocol.ClaimRequestType;
            yield return RedPacketProtocol.ClaimEventType;
            yield return RedPacketProtocol.FailureEventType;
        }

        public bool CanHandleIncomingCommand(FrameworkPacket command)
        {
            return command != null &&
                   command.CommandKind == global::Phinix.Framework.FrameworkCommandKind.Request &&
                   (command.MessageType == RedPacketProtocol.SnapshotType ||
                    command.MessageType == RedPacketProtocol.CreateRequestType ||
                    command.MessageType == RedPacketProtocol.ClaimRequestType);
        }

        public ServerIncomingCommandResult HandleIncomingCommand(FrameworkPacket command, ServerFrameworkContext context)
        {
            switch (command.MessageType)
            {
                case RedPacketProtocol.SnapshotType:
                    SendSnapshotToConnection(context.ConnectionId, context.SessionId, command.GetCorrelationId());
                    break;
                case RedPacketProtocol.CreateRequestType:
                    HandleCreate(command, context);
                    break;
                case RedPacketProtocol.ClaimRequestType:
                    HandleClaim(command, context);
                    break;
            }

            return new ServerIncomingCommandResult
            {
                Action = MessageHandlingResultAction.Handle
            };
        }

        public void Save(string path)
        {
            store.Save(path);
        }

        public void Load(string path)
        {
            store.Load(path);
        }

        private void HandleCreate(FrameworkPacket command, ServerFrameworkContext context)
        {
            RedPacketCreateRequest request;
            try
            {
                request = FrameworkSerialization.DeserializePayload<RedPacketCreateRequest>(command.PayloadJson);
            }
            catch (Exception exception)
            {
                SendFailure(context.ConnectionId, context.SessionId, null, RedPacketProtocol.CreateRequestType, RedPacketFailureReason.InvalidRequest, exception.Message, command.GetCorrelationId());
                return;
            }

            if (!store.TryCreate(context.SenderUuid, request, DateTime.UtcNow, out RedPacketStateSnapshot _, out RedPacketFailureReason failureReason, out string failureMessage))
            {
                SendFailure(context.ConnectionId, context.SessionId, request?.PacketId, RedPacketProtocol.CreateRequestType, failureReason, failureMessage, command.GetCorrelationId());
                return;
            }

            BroadcastSnapshot(context.SessionId, command.GetCorrelationId());
        }

        private void HandleClaim(FrameworkPacket command, ServerFrameworkContext context)
        {
            RedPacketClaimRequest request;
            try
            {
                request = FrameworkSerialization.DeserializePayload<RedPacketClaimRequest>(command.PayloadJson);
            }
            catch (Exception exception)
            {
                SendFailure(context.ConnectionId, context.SessionId, null, RedPacketProtocol.ClaimRequestType, RedPacketFailureReason.InvalidRequest, exception.Message, command.GetCorrelationId());
                return;
            }

            RedPacketClaimEvent claimEvent;
            RedPacketStateSnapshot snapshot;
            RedPacketFailureReason failureReason;
            string failureMessage;
            bool claimed;
            lock (randomLock)
            {
                claimed = store.TryClaim(
                    request?.PacketId,
                    context.SenderUuid,
                    DateTime.UtcNow,
                    random,
                    out claimEvent,
                    out snapshot,
                    out failureReason,
                    out failureMessage);
            }

            if (!claimed)
            {
                SendFailure(context.ConnectionId, context.SessionId, request?.PacketId, RedPacketProtocol.ClaimRequestType, failureReason, failureMessage, command.GetCorrelationId());
                if (snapshot != null)
                {
                    BroadcastSnapshot(context.SessionId, command.GetCorrelationId());
                }
                return;
            }

            SendClaimEvent(claimEvent, context.SessionId, command.GetCorrelationId());
            BroadcastSnapshot(context.SessionId, command.GetCorrelationId());
        }

        private void SendSnapshotToConnection(string connectionId, string sessionId, string correlationId)
        {
            if (string.IsNullOrEmpty(connectionId))
            {
                return;
            }

            RedPacketSnapshotPayload payload = new RedPacketSnapshotPayload
            {
                Packets = store.GetSnapshot(DateTime.UtcNow).ToList()
            };
            FrameworkPacket packet = RedPacketProtocol.CreateServerCommand(
                RedPacketProtocol.SnapshotType,
                global::Phinix.Framework.FrameworkCommandKind.State,
                sessionId,
                payload,
                correlationId ?? Guid.NewGuid().ToString());
            packet.SetStateKind(FrameworkMetadataStateKinds.Snapshot);
            packet.SetSnapshotVersion(payload.Packets.Count == 0 ? 0L : payload.Packets.Max(item => item.CreatedAtUtcTicks));
            packetDispatcher.Send(connectionId, packet, ExtensionId);
        }

        private void BroadcastSnapshot(string sessionId, string correlationId)
        {
            foreach (string connectionId in userManager.GetConnections() ?? Array.Empty<string>())
            {
                SendSnapshotToConnection(connectionId, sessionId, correlationId);
            }
        }

        private void SendClaimEvent(RedPacketClaimEvent claimEvent, string sessionId, string correlationId)
        {
            if (claimEvent == null)
            {
                return;
            }

            FrameworkPacket packet = RedPacketProtocol.CreateServerCommand(
                RedPacketProtocol.ClaimEventType,
                global::Phinix.Framework.FrameworkCommandKind.Event,
                sessionId,
                claimEvent,
                correlationId ?? claimEvent.PacketId);

            HashSet<string> targetConnections = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (userManager.TryGetConnection(claimEvent.ClaimerUuid, out string claimerConnection))
            {
                targetConnections.Add(claimerConnection);
            }
            if (userManager.TryGetConnection(claimEvent.SenderUuid, out string senderConnection))
            {
                targetConnections.Add(senderConnection);
            }

            foreach (string connectionId in targetConnections)
            {
                packetDispatcher.Send(connectionId, packet, ExtensionId);
            }
        }

        private void SendFailure(string connectionId, string sessionId, string packetId, string requestType, RedPacketFailureReason reason, string message, string correlationId)
        {
            if (string.IsNullOrEmpty(connectionId))
            {
                return;
            }

            RedPacketFailureEvent payload = new RedPacketFailureEvent
            {
                PacketId = packetId,
                RequestType = requestType,
                Reason = reason,
                Message = message
            };
            FrameworkPacket packet = RedPacketProtocol.CreateServerCommand(
                RedPacketProtocol.FailureEventType,
                global::Phinix.Framework.FrameworkCommandKind.Response,
                sessionId,
                payload,
                correlationId ?? packetId ?? Guid.NewGuid().ToString());
            packetDispatcher.Send(connectionId, packet, ExtensionId);
        }
    }
}
