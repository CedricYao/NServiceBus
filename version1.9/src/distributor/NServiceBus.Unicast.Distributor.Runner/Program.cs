using System;
using Common.Logging;

namespace NServiceBus.Unicast.Distributor.Runner
{
	/// <summary>
	/// Application for creating and executing a <see cref="Distributor"/>.
	/// </summary>
    class Program
    {
        static void Main()
        {
            LogManager.GetLogger("hello").Debug("Started.");

            try
            {
                string nameSpace = System.Configuration.ConfigurationManager.AppSettings["NameSpace"];
                string serialization = System.Configuration.ConfigurationManager.AppSettings["Serialization"];

                Func<Configure, Configure> func;

                switch(serialization)
                {
                    case "xml":
                        func = cfg => cfg.XmlSerializer(nameSpace);
                        break;
                    case "binary":
                        func = cfg => cfg.BinarySerializer();
                        break;
                    default:
                        throw new ConfigurationException("Serialization can only be one of 'interfaces', 'xml', or 'binary'.");
                }

                Initalizer.Init(func);
            }
            catch (Exception e)
            {
                LogManager.GetLogger("hello").Fatal("Exiting", e);
            }
            finally
            {
                Console.Read();
            }
        }
    }
}