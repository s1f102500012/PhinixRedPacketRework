using System;
using System.Collections.Generic;
using System.Linq;
using PhinixClient;
using PhinixClient.Framework;
using RimWorld;
using UnityEngine;
using UserManagement;
using Utils;
using Utils.Framework;
using Verse;
using Thing = Verse.Thing;

namespace Natsuki.PhinixRedPacketRework.Client
{
    [PhinixExtension(RedPacketProtocol.Capability)]
    public sealed class RedPacketClientExtension :
        IPhinixExtensionModule,
        IActivatablePhinixExtensionModule,
        ICapabilityProvider,
        IClientCommandHandler,
        IClientOutgoingCommandHandler,
        IMainTabProvider,
        IClientSettingsPanelProvider,
        IBadgeProvider
    {
        private const float DefaultSpacing = 8f;
        private const float RowHeight = 32f;
        private const float PacketRowHeight = 82f;
        private const string NotificationSettingKey = "redpacket.notifications";
        private const string AllItemsSettingKey = "redpacket.allItems";
        private const string DropCurrentMapSettingKey = "redpacket.dropCurrentMap";
        private const string ReturnedPacketIdsSettingKey = "redpacket.returnedPacketIds";

        private readonly RedPacketClientState state = new RedPacketClientState();
        private readonly HashSet<string> seenPacketIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> returnedPacketIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly object itemCacheLock = new object();

        private IFrameworkClientTransport frameworkTransport;
        private IFrameworkClientCommandTransport commandTransport;
        private IFrameworkClientLifecycle lifecycle;
        private IClientSessionContext sessionContext;
        private IClientSettingsContext settingsContext;
        private IClientUserDirectory userDirectory;
        private IClientMainThreadDispatcher mainThreadDispatcher;
        private IClientUserEventStream userEvents;
        private Action<string, LogLevel> hostLog;

        private EventHandler<FrameworkCompatibilityModeChangedEventArgs> compatibilityChangedHandler;
        private EventHandler disconnectedHandler;

        private List<RedPacketItemStack> availableItems = new List<RedPacketItemStack>();
        private Vector2 itemScrollPosition = Vector2.zero;
        private Vector2 packetScrollPosition = Vector2.zero;
        private string searchText = string.Empty;
        private string selectedItemKey = string.Empty;
        private string selectedAmountBuffer = "1";
        private string packetCountBuffer = "1";
        private int selectedAmount = 1;
        private int packetCount = 1;
        private RedPacketKind packetKind = RedPacketKind.Lucky;
        private DateTime nextItemRefreshUtc = DateTime.MinValue;
        private bool hasReceivedSnapshot;
        private int claimableCount;
        private string returnedPacketIdsRaw = string.Empty;

        public string ExtensionId => RedPacketProtocol.Capability;

        public int Priority => 1200;

        public string TabLabel => "PRP_Tab".Translate();

        public float TabOrder => 2f;

        public string SectionId => "redpacket.general";

        public float Order => 160f;

        public string BadgeText
        {
            get
            {
                if (sessionContext == null || !sessionContext.LoggedIn)
                {
                    return string.Empty;
                }

                return claimableCount > 0 ? claimableCount.ToString() : string.Empty;
            }
        }

        public void Register(IExtensionBuilder builder)
        {
            builder.AddCapabilityProvider(this);
            builder.AddClientCommandHandler(this);
            builder.RegisterApi<IMainTabProvider>(this);
            builder.RegisterApi<IClientSettingsPanelProvider>(this);
            builder.RegisterApi<IBadgeProvider>(this);
        }

        public void Activate(ExtensionHostContext hostContext)
        {
            if (hostContext == null)
            {
                return;
            }

            hostLog = hostContext.Log;
            frameworkTransport = hostContext.GetRequiredService<IFrameworkClientTransport>();
            commandTransport = hostContext.GetRequiredService<IFrameworkClientCommandTransport>();
            lifecycle = hostContext.GetRequiredService<IFrameworkClientLifecycle>();
            sessionContext = hostContext.GetRequiredService<IClientSessionContext>();
            settingsContext = hostContext.GetRequiredService<IClientSettingsContext>();
            userDirectory = hostContext.GetRequiredService<IClientUserDirectory>();
            mainThreadDispatcher = hostContext.GetRequiredService<IClientMainThreadDispatcher>();
            hostContext.TryGetService(out userEvents);

            if (compatibilityChangedHandler == null)
            {
                compatibilityChangedHandler = (_, args) =>
                {
                    if (args.CompatibilityMode == FrameworkCompatibilityMode.FrameworkV2)
                    {
                        RequestSnapshot();
                    }
                };
            }

            if (disconnectedHandler == null)
            {
                disconnectedHandler = (_, __) =>
                {
                    state.ReplacePackets(Array.Empty<RedPacketStateSnapshot>());
                    hasReceivedSnapshot = false;
                    claimableCount = 0;
                    seenPacketIds.Clear();
                };
            }

            lifecycle.CompatibilityModeChanged -= compatibilityChangedHandler;
            lifecycle.CompatibilityModeChanged += compatibilityChangedHandler;

            if (userEvents != null)
            {
                userEvents.Disconnected -= disconnectedHandler;
                userEvents.Disconnected += disconnectedHandler;
            }

            if (lifecycle.CompatibilityMode == FrameworkCompatibilityMode.FrameworkV2)
            {
                RequestSnapshot();
            }
        }

        public void Shutdown(ExtensionHostContext hostContext)
        {
            if (lifecycle != null && compatibilityChangedHandler != null)
            {
                lifecycle.CompatibilityModeChanged -= compatibilityChangedHandler;
            }

            if (userEvents != null && disconnectedHandler != null)
            {
                userEvents.Disconnected -= disconnectedHandler;
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
                   (command.MessageType == RedPacketProtocol.SnapshotType ||
                    command.MessageType == RedPacketProtocol.ClaimEventType ||
                    command.MessageType == RedPacketProtocol.FailureEventType);
        }

        public ClientIncomingCommandResult HandleIncomingCommand(FrameworkPacket command, ClientFrameworkContext context)
        {
            Action action = () => HandleIncomingCommandOnMainThread(command);
            if (mainThreadDispatcher != null)
            {
                mainThreadDispatcher.Enqueue(action);
            }
            else
            {
                action();
            }

            return new ClientIncomingCommandResult
            {
                Action = MessageHandlingResultAction.Handled
            };
        }

        public bool CanHandleOutgoingCommand(FrameworkPacket command)
        {
            return command?.MessageType?.StartsWith("redpacket.", StringComparison.OrdinalIgnoreCase) == true;
        }

        public ClientOutgoingCommandResult HandleOutgoingCommand(FrameworkPacket command, ClientFrameworkContext context)
        {
            return new ClientOutgoingCommandResult
            {
                Action = MessageHandlingResultAction.Handled,
                Command = command
            };
        }

        public void DrawSettings(Listing_Standard listing, IClientSettingsContext settings)
        {
            bool notifications = settings.Get(NotificationSettingKey, true);
            bool original = notifications;
            listing.CheckboxLabeled("PRP_EnableNotifications".Translate(), ref notifications, "PRP_EnableNotificationsDesc".Translate());
            if (notifications != original)
            {
                settings.Set(NotificationSettingKey, notifications);
            }

            bool allItems = settings.Get(AllItemsSettingKey, false);
            bool originalAllItems = allItems;
            listing.CheckboxLabeled("PRP_AllItems".Translate(), ref allItems, "PRP_AllItemsDesc".Translate());
            if (allItems != originalAllItems)
            {
                settings.Set(AllItemsSettingKey, allItems);
            }

            bool dropCurrentMap = settings.Get(DropCurrentMapSettingKey, false);
            bool originalDropCurrentMap = dropCurrentMap;
            listing.CheckboxLabeled("PRP_DropCurrentMap".Translate(), ref dropCurrentMap, "PRP_DropCurrentMapDesc".Translate());
            if (dropCurrentMap != originalDropCurrentMap)
            {
                settings.Set(DropCurrentMapSettingKey, dropCurrentMap);
            }
        }

        public bool IsVisible(IClientSettingsContext settings)
        {
            return true;
        }

        public void Draw(Rect inRect)
        {
            Widgets.DrawMenuSection(inRect);

            Rect contentRect = inRect.ContractedBy(DefaultSpacing);
            if (sessionContext == null || !sessionContext.LoggedIn)
            {
                DrawCenteredText(contentRect, "PRP_LoginRequired".Translate());
                return;
            }

            if (!HasServerCapability())
            {
                Rect warningRect = contentRect.TopPartPixels(34f);
                Widgets.DrawHighlight(warningRect);
                Widgets.Label(warningRect.ContractedBy(4f), "PRP_ServerMissing".Translate());
                contentRect.yMin += 42f;
            }

            RefreshItemsIfNeeded(false);

            Rect leftRect = contentRect.LeftPartPixels(Mathf.Min(380f, contentRect.width * 0.42f));
            Rect rightRect = new Rect(leftRect.xMax + DefaultSpacing, contentRect.yMin, contentRect.xMax - leftRect.xMax - DefaultSpacing, contentRect.height);

            DrawSendPanel(leftRect);
            DrawPacketPanel(rightRect);
            ProcessExpiredReturns();
        }

        private void HandleIncomingCommandOnMainThread(FrameworkPacket command)
        {
            try
            {
                switch (command.MessageType)
                {
                    case RedPacketProtocol.SnapshotType:
                        HandleSnapshot(command);
                        break;
                    case RedPacketProtocol.ClaimEventType:
                        HandleClaimEvent(command);
                        break;
                    case RedPacketProtocol.FailureEventType:
                        HandleFailureEvent(command);
                        break;
                }
            }
            catch (Exception exception)
            {
                LogWarning("Failed to handle command '" + command?.MessageType + "'.", exception);
            }
        }

        private void HandleSnapshot(FrameworkPacket command)
        {
            RedPacketSnapshotPayload payload = string.IsNullOrEmpty(command.PayloadJson)
                ? new RedPacketSnapshotPayload()
                : FrameworkSerialization.DeserializePayload<RedPacketSnapshotPayload>(command.PayloadJson);

            List<RedPacketStateSnapshot> packets = payload?.Packets ?? new List<RedPacketStateSnapshot>();
            bool notify = hasReceivedSnapshot && settingsContext.Get(NotificationSettingKey, true);
            state.ReplacePackets(packets);
            UpdateClaimableCount();

            foreach (RedPacketStateSnapshot packet in packets)
            {
                if (packet == null || string.IsNullOrEmpty(packet.PacketId))
                {
                    continue;
                }

                bool firstSeen = seenPacketIds.Add(packet.PacketId);
                if (notify && firstSeen && CanClaim(packet))
                {
                    MessageNeutral("PRP_NewPacket".Translate(
                        DisplayNameFor(packet.SenderUuid, packet.SenderDisplayName),
                        RedPacketItemConverter.LabelFor(packet.Item),
                        packet.TotalCount));
                }
            }

            hasReceivedSnapshot = true;
            ProcessExpiredReturns();
        }

        private void HandleClaimEvent(FrameworkPacket command)
        {
            RedPacketClaimEvent payload = FrameworkSerialization.DeserializePayload<RedPacketClaimEvent>(command.PayloadJson);
            if (payload == null || string.IsNullOrEmpty(payload.PacketId))
            {
                return;
            }

            if (!state.MarkClaimEventProcessed(payload.PacketId, payload.ClaimerUuid))
            {
                return;
            }

            string localUuid = sessionContext?.Uuid;
            if (string.Equals(payload.ClaimerUuid, localUuid, StringComparison.OrdinalIgnoreCase))
            {
                state.RemovePendingClaim(payload.PacketId);
                List<Thing> rewardThings = RedPacketItemConverter.ToThings(payload.Item, payload.Amount);
                LookTargets targets = RedPacketItemConverter.DropThings(rewardThings, DropOnCurrentMap);
                MessageNeutral("PRP_Reward".Translate(
                    RedPacketItemConverter.LabelFor(payload.Item),
                    payload.Amount,
                    DisplayNameFor(payload.SenderUuid, null)),
                    targets);
            }

            UpdateClaimableCount();
            RequestSnapshot();
        }

        private void HandleFailureEvent(FrameworkPacket command)
        {
            RedPacketFailureEvent payload = FrameworkSerialization.DeserializePayload<RedPacketFailureEvent>(command.PayloadJson);
            if (payload == null)
            {
                return;
            }

            if (!string.IsNullOrEmpty(payload.PacketId))
            {
                state.RemovePendingClaim(payload.PacketId);
            }
            UpdateClaimableCount();

            string message = string.IsNullOrEmpty(payload.Message) ? payload.Reason.ToString() : payload.Message;
            Messages.Message("PRP_Failed".Translate(message), MessageTypeDefOf.RejectInput, historical: false);
        }

        private void DrawSendPanel(Rect rect)
        {
            Widgets.DrawMenuSection(rect);
            Rect inner = rect.ContractedBy(DefaultSpacing);
            Rect titleRect = inner.TopPartPixels(28f);
            Text.Font = GameFont.Medium;
            Widgets.Label(titleRect, "PRP_SendTitle".Translate());
            Text.Font = GameFont.Small;

            Rect searchRect = new Rect(inner.xMin, titleRect.yMax + DefaultSpacing, inner.width, 28f);
            string previousSearch = searchText;
            searchText = Widgets.TextField(searchRect, searchText);
            if (!string.Equals(previousSearch, searchText, StringComparison.Ordinal))
            {
                itemScrollPosition = Vector2.zero;
            }

            Rect listRect = new Rect(inner.xMin, searchRect.yMax + DefaultSpacing, inner.width, Mathf.Max(120f, inner.height - 224f));
            DrawItemList(listRect);

            Rect controlsRect = new Rect(inner.xMin, listRect.yMax + DefaultSpacing, inner.width, inner.yMax - listRect.yMax - DefaultSpacing);
            DrawSendControls(controlsRect);
        }

        private void DrawItemList(Rect rect)
        {
            List<RedPacketItemStack> filtered = availableItems
                .Where(stack => stack.Count > 0 && (string.IsNullOrEmpty(searchText) || stack.Label.IndexOf(searchText, StringComparison.InvariantCultureIgnoreCase) >= 0))
                .ToList();

            Widgets.DrawMenuSection(rect);
            if (filtered.Count == 0)
            {
                DrawCenteredText(rect, "PRP_NoItems".Translate());
                return;
            }

            Rect viewRect = new Rect(0f, 0f, rect.width - 16f, filtered.Count * RowHeight);
            Widgets.BeginScrollView(rect, ref itemScrollPosition, viewRect);
            for (int index = 0; index < filtered.Count; index++)
            {
                RedPacketItemStack stack = filtered[index];
                Rect rowRect = new Rect(0f, index * RowHeight, viewRect.width, RowHeight);
                bool selected = string.Equals(selectedItemKey, KeyFor(stack), StringComparison.OrdinalIgnoreCase);
                if (selected)
                {
                    Widgets.DrawHighlightSelected(rowRect);
                }
                else if (Mouse.IsOver(rowRect))
                {
                    Widgets.DrawHighlight(rowRect);
                }

                Widgets.Label(new Rect(rowRect.xMin + 6f, rowRect.yMin + 5f, rowRect.width - 86f, 24f), "PRP_RowItem".Translate(stack.Label, stack.Count));
                if (Widgets.ButtonText(new Rect(rowRect.xMax - 76f, rowRect.yMin + 3f, 72f, 26f), (selected ? "PRP_Selected" : "PRP_Select").Translate()))
                {
                    SelectStack(stack);
                }
            }

            Widgets.EndScrollView();
        }

        private void DrawSendControls(Rect rect)
        {
            RedPacketItemStack selectedStack = SelectedStack();
            float y = rect.yMin;

            Widgets.Label(new Rect(rect.xMin, y, 120f, 24f), "PRP_Amount".Translate());
            Rect amountRect = new Rect(rect.xMin + 124f, y, 72f, 24f);
            selectedAmountBuffer = Widgets.TextField(amountRect, selectedAmountBuffer);
            if (int.TryParse(selectedAmountBuffer, out int parsedAmount))
            {
                selectedAmount = selectedStack == null ? parsedAmount : Mathf.Clamp(parsedAmount, 1, selectedStack.Count);
            }
            y += 30f;

            Widgets.Label(new Rect(rect.xMin, y, 120f, 24f), "PRP_PacketCount".Translate());
            Rect packetCountRect = new Rect(rect.xMin + 124f, y, 72f, 24f);
            packetCountBuffer = Widgets.TextField(packetCountRect, packetCountBuffer);
            if (int.TryParse(packetCountBuffer, out int parsedPacketCount))
            {
                packetCount = Mathf.Max(1, parsedPacketCount);
            }
            y += 34f;

            Widgets.Label(new Rect(rect.xMin, y, 72f, 24f), "PRP_Type".Translate());
            Rect normalRect = new Rect(rect.xMin + 76f, y, 92f, 28f);
            Rect luckyRect = new Rect(normalRect.xMax + DefaultSpacing, y, 92f, 28f);
            if (Widgets.ButtonText(normalRect, "PRP_Normal".Translate()))
            {
                packetKind = RedPacketKind.Normal;
            }
            if (packetKind == RedPacketKind.Normal)
            {
                Widgets.DrawHighlightSelected(normalRect);
            }
            if (Widgets.ButtonText(luckyRect, "PRP_Lucky".Translate()))
            {
                packetKind = RedPacketKind.Lucky;
            }
            if (packetKind == RedPacketKind.Lucky)
            {
                Widgets.DrawHighlightSelected(luckyRect);
            }
            y += 40f;

            if (selectedStack != null)
            {
                Widgets.Label(new Rect(rect.xMin, y, rect.width, 24f), "PRP_RowItem".Translate(selectedStack.Label, selectedAmount));
            }
            y += 30f;

            if (Widgets.ButtonText(new Rect(rect.xMin, y, rect.width, 34f), "PRP_Send".Translate()))
            {
                SendSelectedPacket(selectedStack);
            }
        }

        private void DrawPacketPanel(Rect rect)
        {
            Widgets.DrawMenuSection(rect);
            Rect inner = rect.ContractedBy(DefaultSpacing);
            Rect titleRect = inner.TopPartPixels(28f);
            Text.Font = GameFont.Medium;
            Widgets.Label(titleRect, "PRP_ListTitle".Translate());
            Text.Font = GameFont.Small;

            Rect refreshRect = new Rect(inner.xMax - 96f, titleRect.yMin, 96f, 28f);
            if (Widgets.ButtonText(refreshRect, "PRP_Refresh".Translate()))
            {
                RequestSnapshot();
                RefreshItemsIfNeeded(true);
            }

            Rect listRect = new Rect(inner.xMin, titleRect.yMax + DefaultSpacing, inner.width, inner.height - titleRect.height - DefaultSpacing);
            RedPacketStateSnapshot[] packets = state.GetPackets()
                .Where(ShouldShowPacket)
                .OrderByDescending(CanClaim)
                .ThenBy(packet => packet.Expired)
                .ThenByDescending(packet => packet.CreatedAtUtcTicks)
                .ToArray();

            if (packets.Length == 0)
            {
                DrawCenteredText(listRect, "PRP_NoPackets".Translate());
                return;
            }

            Rect viewRect = new Rect(0f, 0f, listRect.width - 16f, packets.Length * PacketRowHeight);
            Widgets.BeginScrollView(listRect, ref packetScrollPosition, viewRect);
            for (int index = 0; index < packets.Length; index++)
            {
                DrawPacketRow(new Rect(0f, index * PacketRowHeight, viewRect.width, PacketRowHeight - 4f), packets[index]);
            }
            Widgets.EndScrollView();
        }

        private void DrawPacketRow(Rect rowRect, RedPacketStateSnapshot packet)
        {
            Widgets.DrawMenuSection(rowRect);
            Rect inner = rowRect.ContractedBy(6f);
            string itemLabel = RedPacketItemConverter.LabelFor(packet.Item);
            string sender = DisplayNameFor(packet.SenderUuid, packet.SenderDisplayName);
            Widgets.Label(new Rect(inner.xMin, inner.yMin, inner.width - 112f, 22f), "PRP_RowItem".Translate(itemLabel, packet.TotalCount));
            Widgets.Label(new Rect(inner.xMin, inner.yMin + 22f, inner.width - 112f, 22f), "PRP_RowSender".Translate(sender));
            Widgets.Label(new Rect(inner.xMin, inner.yMin + 44f, inner.width - 112f, 22f), "PRP_RowRemaining".Translate(
                packet.RemainingCount,
                packet.TotalCount,
                packet.RemainingPackets,
                packet.TotalPackets,
                packet.Kind == RedPacketKind.Lucky ? "PRP_Lucky".Translate() : "PRP_Normal".Translate()));

            Rect rightRect = new Rect(inner.xMax - 104f, inner.yMin, 104f, inner.height);
            string status = PacketStatus(packet);
            Widgets.Label(new Rect(rightRect.xMin, rightRect.yMin, rightRect.width, 24f), status);
            Widgets.Label(new Rect(rightRect.xMin, rightRect.yMin + 24f, rightRect.width, 22f), FormatRemaining(packet));

            Rect buttonRect = new Rect(rightRect.xMin, rightRect.yMax - 28f, rightRect.width, 28f);
            bool canClaim = CanClaim(packet);
            if (canClaim)
            {
                if (Widgets.ButtonText(buttonRect, state.IsPendingClaim(packet.PacketId) ? "PRP_Pending".Translate() : "PRP_Claim".Translate()))
                {
                    SendClaim(packet);
                }
            }
            else
            {
                GUI.color = Color.gray;
                Widgets.ButtonText(buttonRect, status, doMouseoverSound: false);
                GUI.color = Color.white;
            }
        }

        private void SendSelectedPacket(RedPacketItemStack selectedStack)
        {
            if (!HasServerCapability())
            {
                Messages.Message("PRP_ServerMissing".Translate(), MessageTypeDefOf.RejectInput, historical: false);
                return;
            }

            if (selectedStack == null || selectedAmount <= 0)
            {
                Messages.Message("PRP_SelectItem".Translate(), MessageTypeDefOf.RejectInput, historical: false);
                return;
            }

            selectedAmount = Mathf.Clamp(selectedAmount, 1, selectedStack.Count);
            if (packetCount <= 0)
            {
                Messages.Message("PRP_InvalidPacketCount".Translate(), MessageTypeDefOf.RejectInput, historical: false);
                return;
            }

            if (packetCount > selectedAmount)
            {
                Messages.Message("PRP_PacketCountTooLarge".Translate(), MessageTypeDefOf.RejectInput, historical: false);
                return;
            }

            Thing sample = selectedStack.Things.FirstOrDefault(thing => thing != null && !thing.Destroyed);
            if (sample == null)
            {
                Messages.Message("PRP_SelectItem".Translate(), MessageTypeDefOf.RejectInput, historical: false);
                return;
            }

            if (sample is MinifiedThing)
            {
                Messages.Message("PRP_MinifiedRejected".Translate(), MessageTypeDefOf.RejectInput, historical: false);
                return;
            }

            RedPacketItemSnapshot item = RedPacketItemConverter.FromThing(sample, selectedAmount);
            string packetId = Guid.NewGuid().ToString();
            RedPacketCreateRequest request = new RedPacketCreateRequest
            {
                PacketId = packetId,
                SenderDisplayName = DisplayNameFor(sessionContext.Uuid, null),
                Item = item,
                PacketCount = packetCount,
                Kind = packetKind
            };

            FrameworkPacket command = RedPacketProtocol.CreateCommand(
                RedPacketProtocol.CreateRequestType,
                sessionContext.SessionId,
                sessionContext.Uuid,
                request);

            List<Thing> selectedThings = selectedStack.PopSelected().ToList();
            if (!commandTransport.TryHandleOutgoingCommand(command))
            {
                RestoreThings(selectedThings);
                Messages.Message("PRP_ServerMissing".Translate(), MessageTypeDefOf.RejectInput, historical: false);
                RefreshItemsIfNeeded(true);
                return;
            }

            foreach (Thing thing in selectedThings)
            {
                if (thing != null && !thing.Destroyed)
                {
                    thing.Destroy(DestroyMode.Vanish);
                }
            }

            selectedItemKey = string.Empty;
            selectedAmount = 1;
            selectedAmountBuffer = "1";
            packetCount = 1;
            packetCountBuffer = "1";
            RefreshItemsIfNeeded(true);
            MessageNeutral("PRP_Sent".Translate(RedPacketItemConverter.LabelFor(item), item.Count));
        }

        private void SendClaim(RedPacketStateSnapshot packet)
        {
            if (packet == null || state.IsPendingClaim(packet.PacketId))
            {
                return;
            }

            if (RedPacketItemConverter.DefFor(packet.Item)?.thingClass == typeof(MinifiedThing))
            {
                Messages.Message("PRP_MinifiedRejected".Translate(), MessageTypeDefOf.RejectInput, historical: false);
                return;
            }

            state.AddPendingClaim(packet.PacketId);
            UpdateClaimableCount();
            RedPacketClaimRequest request = new RedPacketClaimRequest { PacketId = packet.PacketId };
            FrameworkPacket command = RedPacketProtocol.CreateCommand(
                RedPacketProtocol.ClaimRequestType,
                sessionContext.SessionId,
                sessionContext.Uuid,
                request);

            if (!commandTransport.TryHandleOutgoingCommand(command))
            {
                state.RemovePendingClaim(packet.PacketId);
                UpdateClaimableCount();
                Messages.Message("PRP_ServerMissing".Translate(), MessageTypeDefOf.RejectInput, historical: false);
            }
        }

        private void RequestSnapshot()
        {
            if (commandTransport == null || sessionContext == null || !sessionContext.LoggedIn || !HasServerCapability())
            {
                return;
            }

            FrameworkPacket command = RedPacketProtocol.CreateCommand(
                RedPacketProtocol.SnapshotType,
                sessionContext.SessionId,
                sessionContext.Uuid,
                null);
            if (!commandTransport.TryHandleOutgoingCommand(command))
            {
                LogWarning("Snapshot request was not handled by the command pipeline.");
            }
        }

        private void RefreshItemsIfNeeded(bool force)
        {
            if (!force && DateTime.UtcNow < nextItemRefreshUtc)
            {
                return;
            }

            nextItemRefreshUtc = DateTime.UtcNow.AddSeconds(1);
            IEnumerable<Map> homeMaps = Find.Maps == null
                ? Enumerable.Empty<Map>()
                : Find.Maps.Where(map => map != null && map.IsPlayerHome);

            IEnumerable<Thing> things;
            if (AllItemsTradable)
            {
                things = homeMaps.SelectMany(map => map.listerThings.AllThings);
            }
            else
            {
                things = homeMaps
                    .SelectMany(map => map.haulDestinationManager.AllGroups)
                    .SelectMany(group => group.HeldThings);
            }

            List<RedPacketItemStack> grouped = RedPacketItemStack.GroupThings(things.Where(IsTradableRedPacketThing));
            lock (itemCacheLock)
            {
                availableItems = grouped;
            }

            RedPacketItemStack selected = SelectedStack();
            if (selected == null)
            {
                selectedItemKey = string.Empty;
                return;
            }

            selectedAmount = Mathf.Clamp(selectedAmount, 1, selected.Count);
            selectedAmountBuffer = selectedAmount.ToString();
        }

        private static bool IsTradableRedPacketThing(Thing thing)
        {
            return thing != null &&
                   !thing.Destroyed &&
                   thing.stackCount > 0 &&
                   thing.def != null &&
                   thing.def.category == ThingCategory.Item &&
                   !thing.def.IsCorpse &&
                   !(thing is MinifiedThing);
        }

        private RedPacketItemStack SelectedStack()
        {
            lock (itemCacheLock)
            {
                return availableItems.FirstOrDefault(stack => string.Equals(KeyFor(stack), selectedItemKey, StringComparison.OrdinalIgnoreCase));
            }
        }

        private void SelectStack(RedPacketItemStack stack)
        {
            if (stack == null)
            {
                selectedItemKey = string.Empty;
                return;
            }

            selectedItemKey = KeyFor(stack);
            selectedAmount = Mathf.Clamp(selectedAmount, 1, stack.Count);
            selectedAmountBuffer = selectedAmount.ToString();
            if (packetCount > selectedAmount)
            {
                packetCount = selectedAmount;
                packetCountBuffer = packetCount.ToString();
            }
        }

        private static string KeyFor(RedPacketItemStack stack)
        {
            if (stack == null || stack.ThingDef == null)
            {
                return string.Empty;
            }

            string stuff = stack.StuffDef?.defName ?? string.Empty;
            string style = stack.StyleDef?.defName ?? string.Empty;
            return stack.ThingDef.defName + "|" + stuff + "|" + style + "|" + stack.Label;
        }

        private bool CanClaim(RedPacketStateSnapshot packet)
        {
            if (packet == null || sessionContext == null)
            {
                return false;
            }

            string localUuid = sessionContext.Uuid;
            DateTime now = DateTime.UtcNow;
            return !packet.Expired &&
                   packet.ExpiresAtUtcTicks > now.Ticks &&
                   packet.RemainingCount > 0 &&
                   packet.RemainingPackets > 0 &&
                   !string.Equals(packet.SenderUuid, localUuid, StringComparison.OrdinalIgnoreCase) &&
                   !RedPacketClientState.HasClaimed(packet, localUuid) &&
                   !state.IsPendingClaim(packet.PacketId);
        }

        private bool ShouldShowPacket(RedPacketStateSnapshot packet)
        {
            if (packet == null)
            {
                return false;
            }

            bool mine = string.Equals(packet.SenderUuid, sessionContext?.Uuid, StringComparison.OrdinalIgnoreCase);
            if (packet.Expired)
            {
                return mine && packet.RemainingCount > 0 && !HasReturnedPacket(packet.PacketId);
            }

            return packet.RemainingCount > 0 || mine || RedPacketClientState.HasClaimed(packet, sessionContext?.Uuid);
        }

        private string PacketStatus(RedPacketStateSnapshot packet)
        {
            if (packet.Expired)
            {
                return "PRP_Expired".Translate();
            }

            if (packet.RemainingCount <= 0 || packet.RemainingPackets <= 0)
            {
                return "PRP_Empty".Translate();
            }

            if (string.Equals(packet.SenderUuid, sessionContext?.Uuid, StringComparison.OrdinalIgnoreCase))
            {
                return "PRP_Yours".Translate();
            }

            if (RedPacketClientState.HasClaimed(packet, sessionContext?.Uuid))
            {
                return "PRP_Claimed".Translate();
            }

            if (state.IsPendingClaim(packet.PacketId))
            {
                return "PRP_Pending".Translate();
            }

            return "PRP_Claim".Translate();
        }

        private string FormatRemaining(RedPacketStateSnapshot packet)
        {
            long ticks = packet.ExpiresAtUtcTicks - DateTime.UtcNow.Ticks;
            if (ticks <= 0)
            {
                return "PRP_Expired".Translate();
            }

            TimeSpan remaining = new TimeSpan(ticks);
            if (remaining.TotalHours >= 1)
            {
                return "PRP_RowTime".Translate(((int)remaining.TotalHours) + "h");
            }

            return "PRP_RowTime".Translate(Mathf.Max(1, (int)remaining.TotalMinutes) + "m");
        }

        private void ProcessExpiredReturns()
        {
            string localUuid = sessionContext?.Uuid;
            if (string.IsNullOrEmpty(localUuid))
            {
                return;
            }

            foreach (RedPacketStateSnapshot packet in state.GetPackets())
            {
                if (packet == null ||
                    !packet.Expired ||
                    packet.RemainingCount <= 0 ||
                    !string.Equals(packet.SenderUuid, localUuid, StringComparison.OrdinalIgnoreCase) ||
                    HasReturnedPacket(packet.PacketId) ||
                    !state.MarkExpiredReturned(packet.PacketId))
                {
                    continue;
                }

                List<Thing> returned = RedPacketItemConverter.ToThings(packet.Item, packet.RemainingCount);
                LookTargets targets = RedPacketItemConverter.DropThings(returned, DropOnCurrentMap);
                MarkReturnedPacket(packet.PacketId);
                MessageNeutral("PRP_Returned".Translate(
                    RedPacketItemConverter.LabelFor(packet.Item),
                    packet.RemainingCount),
                    targets);
            }
        }

        private bool HasServerCapability()
        {
            return frameworkTransport != null && frameworkTransport.HasRemoteCapability(RedPacketProtocol.Capability);
        }

        private bool AllItemsTradable => settingsContext != null && settingsContext.Get(AllItemsSettingKey, false);

        private bool DropOnCurrentMap => settingsContext != null && settingsContext.Get(DropCurrentMapSettingKey, false);

        private void UpdateClaimableCount()
        {
            claimableCount = sessionContext == null || !sessionContext.LoggedIn
                ? 0
                : state.CountClaimable(sessionContext.Uuid, DateTime.UtcNow);
        }

        private void LogWarning(string message, Exception exception = null)
        {
            if (hostLog == null)
            {
                return;
            }

            string text = exception == null
                ? "[PhinixRedPacket] " + message
                : "[PhinixRedPacket] " + message + Environment.NewLine + exception;
            hostLog(text, LogLevel.WARNING);
        }

        private bool HasReturnedPacket(string packetId)
        {
            if (settingsContext == null || string.IsNullOrEmpty(packetId))
            {
                return false;
            }

            string returned = settingsContext.Get(ReturnedPacketIdsSettingKey, string.Empty);
            RefreshReturnedPacketCache(returned);
            return returnedPacketIds.Contains(packetId);
        }

        private void MarkReturnedPacket(string packetId)
        {
            if (settingsContext == null || string.IsNullOrEmpty(packetId) || HasReturnedPacket(packetId))
            {
                return;
            }

            string returned = settingsContext.Get(ReturnedPacketIdsSettingKey, string.Empty);
            string updated = string.IsNullOrEmpty(returned) ? packetId : returned + "|" + packetId;
            settingsContext.Set(ReturnedPacketIdsSettingKey, updated);
            RefreshReturnedPacketCache(updated);
            returnedPacketIds.Add(packetId);
        }

        private void RefreshReturnedPacketCache(string raw)
        {
            raw = raw ?? string.Empty;
            if (string.Equals(raw, returnedPacketIdsRaw, StringComparison.Ordinal))
            {
                return;
            }

            returnedPacketIdsRaw = raw;
            returnedPacketIds.Clear();
            foreach (string packetId in raw.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries))
            {
                returnedPacketIds.Add(packetId);
            }
        }

        private string DisplayNameFor(string uuid, string fallback)
        {
            if (!string.IsNullOrEmpty(uuid) && userDirectory != null && userDirectory.TryGetUser(uuid, out ImmutableUser user) && !string.IsNullOrEmpty(user.DisplayName))
            {
                return user.DisplayName;
            }

            if (!string.IsNullOrEmpty(fallback))
            {
                return fallback;
            }

            return string.IsNullOrEmpty(uuid) ? "???" : uuid;
        }

        private static void DrawCenteredText(Rect rect, string text)
        {
            TextAnchor anchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(rect, text);
            Text.Anchor = anchor;
        }

        private static void MessageNeutral(string text, LookTargets targets = null)
        {
            if (targets != null)
            {
                Messages.Message(text, targets, MessageTypeDefOf.PositiveEvent, historical: false);
                return;
            }

            Messages.Message(text, MessageTypeDefOf.PositiveEvent, historical: false);
        }

        private static void RestoreThings(IEnumerable<Thing> things)
        {
            Map map = Find.AnyPlayerHomeMap ?? Find.CurrentMap;
            if (map == null)
            {
                return;
            }

            IntVec3 dropSpot = DropCellFinder.TradeDropSpot(map);
            foreach (Thing thing in things ?? Enumerable.Empty<Thing>())
            {
                if (thing == null || thing.Destroyed || thing.Spawned)
                {
                    continue;
                }

                GenPlace.TryPlaceThing(thing, dropSpot, map, ThingPlaceMode.Near);
            }
        }
    }
}
