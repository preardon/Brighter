namespace Paramore.Brighter.MessagingGateway.AzureServiceBus
{
    public class AzureServiceBusConfiguration
    {
        public AzureServiceBusConfiguration(string connectionString, bool ackOnRead = false, bool useAsbForRequeue )
        {
            ConnectionString = connectionString;
            AckOnRead = ackOnRead;
            UseAsbForRequeue = useAsbForRequeue;
        }

        public string ConnectionString { get; }

        /// <summary>
        /// When set to true this will Change RecieveMode from ReceiveAndDelete to PeekAndLock
        /// </summary>
        public bool AckOnRead{ get; }

        /// <summary>
        /// Use The Properties Set up in ASB Subscription Instead of Reposting a new Message to the Bus
        /// </summary>
        public bool UseAsbForRequeue { get; }
    }
}
