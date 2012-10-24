using System;
using System.Collections.Generic;
using System.Configuration;
using Microsoft.ServiceBus;
using Microsoft.ServiceBus.Messaging;
using Microsoft.WindowsAzure.ServiceRuntime;
using NServiceBus.Config;
using NServiceBus.Config.Conventions;
using NServiceBus.Unicast.Queuing.Azure.ServiceBus;

namespace NServiceBus
{
    public static class ConfigureAzureOnPremisesServiceBusMessageQueue
    {
        public static Configure AzureSelfHostedServiceBusMessageQueue(this Configure config)
        {
            var configSection = Configure.GetConfigSection<AzureOnPremisesServiceBusQueueConfig>();

            if (configSection == null)
                throw new ConfigurationErrorsException("No AzureServiceBusQueueConfig configuration section found");

            Address.InitializeAddressMode(AddressMode.Remote);

            var serviceNamespace = configSection.ServiceNamespace;
            var serverFqdn = configSection.ServerFQDN;
            var httpsPort = configSection.SBHttpsPort;
            var tcpPort = configSection.SBTcpPort;

            var rootAddressManagement = ServiceBusEnvironment.CreatePathBasedServiceUri("sb", serviceNamespace, string.Format("{0}:{1}", serverFqdn, httpsPort));
            var rootAddressRuntime = ServiceBusEnvironment.CreatePathBasedServiceUri("sb", serviceNamespace, string.Format("{0}:{1}", serverFqdn, tcpPort));
            var tokenProvider = TokenProvider.CreateWindowsTokenProvider(new List<Uri>() {rootAddressManagement});

            var namespaceManagerSettings = new NamespaceManagerSettings { TokenProvider = tokenProvider };
            var namespaceClient = new NamespaceManager(rootAddressManagement, namespaceManagerSettings);
            var messagingFactorySettings = new MessagingFactorySettings { TokenProvider = tokenProvider };
            var factory = MessagingFactory.Create(rootAddressRuntime, messagingFactorySettings);

            Address.OverrideDefaultMachine(serverFqdn);

            config.Configurer.RegisterSingleton<NamespaceManager>(namespaceClient); 
            config.Configurer.RegisterSingleton<MessagingFactory>(factory);

            config.Configurer.ConfigureComponent<AzureServiceBusMessageQueue>(DependencyLifecycle.SingleInstance);

            Configure.Instance.Configurer.ConfigureProperty<AzureServiceBusMessageQueue>(t => t.LockDuration, TimeSpan.FromMilliseconds(configSection.LockDuration));
            Configure.Instance.Configurer.ConfigureProperty<AzureServiceBusMessageQueue>(t => t.MaxSizeInMegabytes, configSection.MaxSizeInMegabytes);
            Configure.Instance.Configurer.ConfigureProperty<AzureServiceBusMessageQueue>(t => t.RequiresDuplicateDetection, configSection.RequiresDuplicateDetection);
            Configure.Instance.Configurer.ConfigureProperty<AzureServiceBusMessageQueue>(t => t.RequiresSession, configSection.RequiresSession);
            Configure.Instance.Configurer.ConfigureProperty<AzureServiceBusMessageQueue>(t => t.DefaultMessageTimeToLive, TimeSpan.FromMilliseconds(configSection.DefaultMessageTimeToLive));
            Configure.Instance.Configurer.ConfigureProperty<AzureServiceBusMessageQueue>(t => t.EnableDeadLetteringOnMessageExpiration, configSection.EnableDeadLetteringOnMessageExpiration);
            Configure.Instance.Configurer.ConfigureProperty<AzureServiceBusMessageQueue>(t => t.DuplicateDetectionHistoryTimeWindow, TimeSpan.FromMilliseconds(configSection.DuplicateDetectionHistoryTimeWindow));
            Configure.Instance.Configurer.ConfigureProperty<AzureServiceBusMessageQueue>(t => t.MaxDeliveryCount, configSection.MaxDeliveryCount);
            Configure.Instance.Configurer.ConfigureProperty<AzureServiceBusMessageQueue>(t => t.EnableBatchedOperations, configSection.EnableBatchedOperations);

            if (!string.IsNullOrEmpty(configSection.QueueName))
            {
                Configure.Instance.DefineEndpointName(configSection.QueuePerInstance
                                                          ? QueueIndividualizer.Individualize(configSection.QueueName)
                                                          : configSection.QueueName);
            }
            else if (RoleEnvironment.IsAvailable)
            {
                Configure.Instance.DefineEndpointName(RoleEnvironment.CurrentRoleInstance.Role.Name);
            }
            Address.InitializeLocalAddress(Configure.EndpointName);


            return config;
        }
    }
}