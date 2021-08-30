// MIT License

// MasterDegree:
// Copyright (c) 2020 Adam "Deusald" Orliński

// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:

// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.

// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using System;
using System.Net;
using DarkRift;
using DarkRift.Client;
using DarkRift.Dispatching;
using UnityEngine;

namespace MasterDegree
{
    public class DrClient
    {
        #region Properties

        public DarkRiftClient Client { get; }

        #endregion Properties

        #region Variables

        /// <summary>
        ///     Event fired when a message is received.
        /// </summary>
        public event EventHandler<MessageReceivedEventArgs> MessageReceived;

        /// <summary>
        ///     Event fired when we disconnect form the server.
        /// </summary>
        public event EventHandler<DisconnectedEventArgs> Disconnected;

        private readonly Dispatcher _Dispatcher;

        #endregion Variables

        #region Init Methods

        public DrClient()
        {
            Client                 =  new DarkRiftClient(GetObjectCacheSettings());
            _Dispatcher            =  new Dispatcher(true);
            Client.MessageReceived += ClientOnMessageReceived;
            Client.Disconnected    += ClientOnDisconnected;
        }

        #endregion Init Methods

        #region Public Methods

        public void Connect(IPAddress ip, int port)
        {
            Client.Connect(ip, port, false);
        }

        public void Update()
        {
            _Dispatcher.ExecuteDispatcherTasks();
        }

        public void Close()
        {
            Client.MessageReceived -= ClientOnMessageReceived;
            Client.Disconnected    -= ClientOnDisconnected;

            Client.Dispose();
            _Dispatcher.Dispose();
        }

        #endregion Public Methods

        #region Private Methods

        private void ClientOnMessageReceived(object sender, MessageReceivedEventArgs e)
        {
            Message                  message = e.GetMessage();
            MessageReceivedEventArgs args    = MessageReceivedEventArgs.Create(message, e.SendMode);

            _Dispatcher.InvokeAsync(
                () =>
                {
                    EventHandler<MessageReceivedEventArgs> handler = MessageReceived;
                    if (handler != null)
                    {
                        handler.Invoke(sender, args);
                    }

                    message.Dispose();
                    args.Dispose();
                }
            );
        }

        private void ClientOnDisconnected(object sender, DisconnectedEventArgs e)
        {
            if (!e.LocalDisconnect)
                Debug.Log("Disconnected from server, error: " + e.Error);

            _Dispatcher.InvokeAsync(
                () =>
                {
                    EventHandler<DisconnectedEventArgs> handler = Disconnected;
                    if (handler != null)
                    {
                        handler.Invoke(sender, e);
                    }
                }
            );
        }

        private ClientObjectCacheSettings GetObjectCacheSettings()
        {
            return new ClientObjectCacheSettings
            {
                MaxWriters               = 2,
                MaxReaders               = 2,
                MaxMessages              = 4,
                MaxMessageBuffers        = 4,
                MaxSocketAsyncEventArgs  = 32,
                MaxActionDispatcherTasks = 16,
                MaxAutoRecyclingArrays   = 4,

                ExtraSmallMemoryBlockSize = 16,
                MaxExtraSmallMemoryBlocks = 2,
                SmallMemoryBlockSize      = 64,
                MaxSmallMemoryBlocks      = 2,
                MediumMemoryBlockSize     = 256,
                MaxMediumMemoryBlocks     = 2,
                LargeMemoryBlockSize      = 1024,
                MaxLargeMemoryBlocks      = 2,
                ExtraLargeMemoryBlockSize = 4096,
                MaxExtraLargeMemoryBlocks = 2,

                MaxMessageReceivedEventArgs = 4
            };
        }

        #endregion Private Methods
    }
}