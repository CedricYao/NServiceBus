namespace NServiceBus.Gateway.Receiving
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Channels;

    public class ConventionBasedChannelManager : IMangageReceiveChannels
    {
        public IEnumerable<ReceiveChannel> GetReceiveChannels()
        {
            yield return new ReceiveChannel()
                             {
                                 Address = string.Format("http://localhost/{0}/",Configure.EndpointName),
                                 Type = "Http",
                                 NumberOfWorkerThreads = 1
                             };
        }

        public Channel GetDefaultChannel()
        {
            return GetReceiveChannels().First();
        }
    }
}