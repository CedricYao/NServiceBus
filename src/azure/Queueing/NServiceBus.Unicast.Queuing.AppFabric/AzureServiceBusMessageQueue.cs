﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Threading;
using System.Transactions;
using Microsoft.ServiceBus;
using Microsoft.ServiceBus.Messaging;
using NServiceBus.Unicast.Transport;

namespace NServiceBus.Unicast.Queuing.Azure.ServiceBus
{
    public class AzureServiceBusMessageQueue : IReceiveMessages, ISendMessages
    {
        public const string DefaultIssuerName = "owner";
        public const int DefaultLockDuration = 30000;
        public const long DefaultMaxSizeInMegabytes = 1024;
        public const bool DefaultRequiresDuplicateDetection = false;
        public const bool DefaultRequiresSession = false;
        public const long DefaultDefaultMessageTimeToLive =  92233720368547;
        public const bool DefaultEnableDeadLetteringOnMessageExpiration = false;
        public const int DefaultDuplicateDetectionHistoryTimeWindow = 600000;
        public const int DefaultMaxDeliveryCount = 6;
        public const bool DefaultEnableBatchedOperations = false;
        public const bool DefaultQueuePerInstance = false;
        public const int DefaultBackoffTimeInSeconds = 10;
        public const int DefaultSBTcpPort = 9354;
        public const int DefaultSBHttpsPort = 4446;

        private readonly Dictionary<string, QueueClient> senders = new Dictionary<string, QueueClient>();
        private static readonly object SenderLock = new Object();

        
        private bool useTransactions;
        private QueueClient queueClient;
        private string queueName;

        public TimeSpan LockDuration { get; set; }
        public long MaxSizeInMegabytes { get; set; }
        public bool RequiresDuplicateDetection { get; set; }
        public bool RequiresSession { get; set; }
        public TimeSpan DefaultMessageTimeToLive { get; set; }
        public bool EnableDeadLetteringOnMessageExpiration { get; set; }
        public TimeSpan DuplicateDetectionHistoryTimeWindow { get; set; }
        public int MaxDeliveryCount { get; set; }
        public bool EnableBatchedOperations { get; set; }

        public MessagingFactory Factory { get; set; }
        public NamespaceManager NamespaceClient { get; set; }

        public void Init(string address, bool transactional)
        {
            Init(Address.Parse(address), transactional);
        }

        public void Init(Address address, bool transactional)
        {
            try
            {
                queueName = address.Queue;
                var description = new QueueDescription(queueName)
                                      {
                                           LockDuration= LockDuration,
                                           MaxSizeInMegabytes = MaxSizeInMegabytes, 
                                           RequiresDuplicateDetection = RequiresDuplicateDetection,
                                           RequiresSession = RequiresSession,
                                           DefaultMessageTimeToLive = DefaultMessageTimeToLive,
                                           EnableDeadLetteringOnMessageExpiration = EnableDeadLetteringOnMessageExpiration, 
                                           DuplicateDetectionHistoryTimeWindow = DuplicateDetectionHistoryTimeWindow,
                                           MaxDeliveryCount = MaxDeliveryCount,
                                           EnableBatchedOperations = EnableBatchedOperations
                                      };

                NamespaceClient.CreateQueue(description);
            }
            catch (MessagingEntityAlreadyExistsException)
            {
                // the queue already exists, which is ok
            }

            queueClient = Factory.CreateQueueClient(queueName, ReceiveMode.PeekLock);
            
            useTransactions = transactional;
        }

        public bool HasMessage()
        {
            return true; 
        }

        public TransportMessage Receive()
        {
            BrokeredMessage message = null;

            try{
                message= queueClient.Receive(TimeSpan.FromSeconds(30));
            }
            // back off when we're being throttled
            catch (ServerBusyException)
            {
                Thread.Sleep(TimeSpan.FromSeconds(DefaultBackoffTimeInSeconds)); 
            }

            if(message != null)
            {
                var rawMessage = message.GetBody<byte[]>();
                var t = DeserializeMessage(rawMessage);

                if (!useTransactions || Transaction.Current == null)
                {
                    try
                    {
                        message.Complete();
                    }
                    catch (MessageLockLostException)
                    {
                        // message has been completed by another thread or worker
                    }
                }
                else
                    Transaction.Current.EnlistVolatile(new ReceiveResourceManager(message), EnlistmentOptions.None);

                return t;
            }
            return null;
        }

        public void Send(TransportMessage message, string destination)
        {
            Send(message, Address.Parse(destination));
        }

        public void Send(TransportMessage message, Address address)
        {
            var destination = address.Queue;
                
            QueueClient sender;
            if (!senders.TryGetValue(destination, out sender) )
            {
                lock (SenderLock)
                {
                    if (!senders.TryGetValue(destination, out sender) )
                    {
                            try
                            {
                                sender = Factory.CreateQueueClient(destination);
                                senders[destination] = sender;
                            }
                            catch (MessagingEntityNotFoundException)
                            {
                                throw new QueueNotFoundException { Queue = Address.Parse(destination) };
                            }
                    }
                }
            }

            message.Id = Guid.NewGuid().ToString();
            var rawMessage = SerializeMessage(message);
            
            if (Transaction.Current == null)
                Send(rawMessage, sender);
            else
                Transaction.Current.EnlistVolatile(new SendResourceManager(() => Send(rawMessage, sender)), EnlistmentOptions.None);
           
        }

        private void Send(Byte[] rawMessage, QueueClient sender)
        {
            var numRetries = 0;
            var sent = false;

            while(!sent)
            {
                try
                {
                    var brokeredMessage = new BrokeredMessage(rawMessage);

                    sender.Send(brokeredMessage);

                    sent = true;
                }
                    // back off when we're being throttled
                catch (ServerBusyException)
                {
                    numRetries++;

                    if (numRetries >= MaxDeliveryCount) throw;

                    Thread.Sleep(TimeSpan.FromSeconds(numRetries * DefaultBackoffTimeInSeconds));
                }
            }

        }

        private static byte[] SerializeMessage(TransportMessage message)
        {
            if (message.Headers == null)
                message.Headers = new Dictionary<string, string>();

            if (!message.Headers.ContainsKey(Idforcorrelation))
                message.Headers.Add(Idforcorrelation, null);

            if (String.IsNullOrEmpty(message.Headers[Idforcorrelation]))
                message.Headers[Idforcorrelation] = message.IdForCorrelation;

            using (var stream = new MemoryStream())
            {
                var formatter = new BinaryFormatter();
                formatter.Serialize(stream, message);
                return stream.ToArray();
            }
        }

        private static TransportMessage DeserializeMessage(byte[] rawMessage)
        {
            var formatter = new BinaryFormatter();

            using (var stream = new MemoryStream(rawMessage))
            {
                var message = formatter.Deserialize(stream) as TransportMessage;

                if (message == null)
                    throw new SerializationException("Failed to deserialize message");

                message.Id = GetRealId(message.Headers) ?? message.Id;

                message.IdForCorrelation = GetIdForCorrelation(message.Headers) ?? message.Id;

                return message;
            }
        }

        private static string GetRealId(IDictionary<string, string> headers)
        {
            if (headers.ContainsKey(TransportHeaderKeys.OriginalId))
                return headers[TransportHeaderKeys.OriginalId];

            return null;
        }

        private static string GetIdForCorrelation(IDictionary<string, string> headers)
        {
            if (headers.ContainsKey(Idforcorrelation))
                return headers[Idforcorrelation];

            return null;
        }

        private const string Idforcorrelation = "CorrId";
    }
}
