using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using PhinixClient;
using PhinixClient.Framework;
using Utils.Framework;
using Verse;

namespace Natsuki.PhinixRedPacketRework.Client
{
    [StaticConstructorOnStartup]
    internal static class RedPacketClientBootstrap
    {
        static RedPacketClientBootstrap()
        {
            LongEventHandler.ExecuteWhenFinished(TryInstall);
        }

        private static void TryInstall()
        {
            try
            {
                object frameworkClient = ResolveFrameworkClient();
                if (frameworkClient == null)
                {
                    return;
                }

                object discovered = GetPrivateField(frameworkClient, "discoveredExtensions");
                ExtensionHostContext hostContext = GetPrivateField(frameworkClient, "extensionHostContext") as ExtensionHostContext;
                if (discovered == null || hostContext == null || ContainsExtension(discovered, RedPacketProtocol.Capability))
                {
                    return;
                }

                RedPacketClientExtension module = new RedPacketClientExtension();
                RuntimeExtensionBuilder builder = new RuntimeExtensionBuilder(module.ExtensionId, hostContext, discovered);
                AddToDiscoveredList(discovered, "Extensions", module);
                AddToDiscoveredList(discovered, "Modules", module);
                module.Register(builder);
                module.Activate(hostContext);
                MergeClientCapabilities(frameworkClient, module.GetCapabilities());

                MethodInfo beginNegotiation = frameworkClient.GetType().GetMethod("BeginNegotiation", BindingFlags.Instance | BindingFlags.Public);
                beginNegotiation?.Invoke(frameworkClient, null);
                Log.Message("[PhinixRedPacket] Registered client extension through late bootstrap.");
            }
            catch (Exception exception)
            {
                Log.Warning("[PhinixRedPacket] Late bootstrap failed: " + exception);
            }
        }

        private static object ResolveFrameworkClient()
        {
            Type clientType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("PhinixClient.Client", throwOnError: false))
                .FirstOrDefault(type => type != null);
            if (clientType == null)
            {
                return null;
            }

            FieldInfo instanceField = clientType.GetField("Instance", BindingFlags.Static | BindingFlags.Public);
            object clientInstance = instanceField?.GetValue(null);
            if (clientInstance == null)
            {
                return null;
            }

            PropertyInfo frameworkClientProperty = clientType.GetProperty("FrameworkClient", BindingFlags.Instance | BindingFlags.Public);
            return frameworkClientProperty?.GetValue(clientInstance, null);
        }

        private static object GetPrivateField(object instance, string fieldName)
        {
            return instance?.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(instance);
        }

        private static bool ContainsExtension(object discovered, string extensionId)
        {
            foreach (object extension in GetDiscoveredList(discovered, "Extensions"))
            {
                if (extension is IPhinixExtension phinixExtension &&
                    string.Equals(phinixExtension.ExtensionId, extensionId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static void MergeClientCapabilities(object frameworkClient, IEnumerable<string> extraCapabilities)
        {
            FieldInfo capabilitiesField = frameworkClient.GetType().GetField("capabilities", BindingFlags.Instance | BindingFlags.NonPublic);
            if (capabilitiesField == null)
            {
                return;
            }

            string[] existing = capabilitiesField.GetValue(frameworkClient) as string[] ?? Array.Empty<string>();
            string[] merged = existing
                .Concat(extraCapabilities ?? Enumerable.Empty<string>())
                .Where(capability => !string.IsNullOrEmpty(capability))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(capability => capability, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            capabilitiesField.SetValue(frameworkClient, merged);
        }

        private static IList GetDiscoveredList(object discovered, string propertyName)
        {
            object value = discovered?.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)?.GetValue(discovered, null);
            return value as IList ?? ArrayList.Adapter(Array.Empty<object>());
        }

        private static void AddToDiscoveredList(object discovered, string propertyName, object value)
        {
            IList list = GetDiscoveredList(discovered, propertyName);
            if (!list.Contains(value))
            {
                list.Add(value);
            }
        }

        private sealed class RuntimeExtensionBuilder : IExtensionBuilder
        {
            private readonly object discovered;

            public RuntimeExtensionBuilder(string extensionId, ExtensionHostContext hostContext, object discovered)
            {
                ExtensionId = extensionId;
                HostContext = hostContext;
                this.discovered = discovered;
            }

            public string ExtensionId { get; }

            public ExtensionHostContext HostContext { get; }

            public IExtensionApiRegistry ApiRegistry => HostContext.ApiRegistry;

            public void AddCapabilityProvider(ICapabilityProvider capabilityProvider) => Add("CapabilityProviders", capabilityProvider);

            public void AddMessageInterceptor(IMessageInterceptor interceptor) => Add("MessageInterceptors", interceptor);

            public void AddMessageRenderer(IMessageRenderer renderer) => Add("MessageRenderers", renderer);

            public void AddClientMessageHandler(IClientMessageHandler handler) => Add("ClientMessageHandlers", handler);

            public void AddServerMessageHandler(IServerMessageHandler handler) => Add("ServerMessageHandlers", handler);

            public void AddServerInboundMessageInterceptor(IServerInboundMessageInterceptor interceptor) => Add("ServerInboundMessageInterceptors", interceptor);

            public void AddServerDefaultMessageHandler(IServerDefaultMessageHandler handler) => Add("ServerDefaultMessageHandlers", handler);

            public void AddServerMessageObserver(IServerMessageObserver observer) => Add("ServerMessageObservers", observer);

            public void AddItemCodec(IItemCodec codec) => Add("ItemCodecs", codec);

            public void AddClientCommandHandler(IClientCommandHandler handler) => Add("ClientCommandHandlers", handler);

            public void AddServerCommandHandler(IServerCommandHandler handler) => Add("ServerCommandHandlers", handler);

            public void AddServerInboundCommandInterceptor(IServerInboundCommandInterceptor interceptor) => Add("ServerInboundCommandInterceptors", interceptor);

            public void AddServerDefaultCommandHandler(IServerDefaultCommandHandler handler) => Add("ServerDefaultCommandHandlers", handler);

            public void AddServerCommandObserver(IServerCommandObserver observer) => Add("ServerCommandObservers", observer);

            public void AddServerOutboundPacketInterceptor(IServerOutboundPacketInterceptor interceptor) => Add("ServerOutboundPacketInterceptors", interceptor);

            public void RegisterApi<T>(T implementation) where T : class
            {
                ApiRegistry.RegisterApi(ExtensionId, implementation);
            }

            public bool TryResolveApi<T>(out T implementation) where T : class
            {
                return ApiRegistry.TryResolve(out implementation);
            }

            public IReadOnlyList<T> ResolveApis<T>() where T : class
            {
                return ApiRegistry.ResolveAll<T>();
            }

            private void Add(string propertyName, object value)
            {
                if (value != null)
                {
                    AddToDiscoveredList(discovered, propertyName, value);
                }
            }
        }
    }
}
