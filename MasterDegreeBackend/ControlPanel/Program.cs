using System;
using System.Net;
using DarkRift;
using DarkRift.Client;
using GameLogicCommon;

namespace ControlPanel
{
    internal static class Program
    {
        static void Main()
        {
            DarkRiftClient darkRiftClient = new DarkRiftClient();
            darkRiftClient.Connect(IPAddress.Parse("172.19.134.120"), 31317, false);
            darkRiftClient.MessageReceived += OnMessageReceived;
            
            while (true)
            {
                string input = Console.ReadLine();
                
                SendEmptyMessage(Messages.MessageId.AllocateGame, false, darkRiftClient);
            }
        }

        private static void OnMessageReceived(object sender, MessageReceivedEventArgs e)
        {
            Console.Write($"Message received -> tag {e.Tag}");
        }

        private static void SendMessage(Messages.INetMessage msg, DarkRiftClient client)
        {
            using (DarkRiftWriter writer = DarkRiftWriter.Create())
            {
                using (Message message = Message.Create((ushort)msg.MessageId, writer))
                {
                    msg.Write(writer);
                    Console.WriteLine("Send message");
                    client.SendMessage(message, msg.IsFrequent ? SendMode.Unreliable : SendMode.Reliable);
                }
            }
        }

        private static void SendEmptyMessage(Messages.MessageId messageId, bool isFrequent, DarkRiftClient client)
        {
            using (DarkRiftWriter writer = DarkRiftWriter.Create())
            {
                using (Message message = Message.Create((ushort)messageId, writer))
                {
                    Console.WriteLine("Send empty message");
                    client.SendMessage(message, isFrequent ? SendMode.Unreliable : SendMode.Reliable);
                }
            }
        }
    }
}