using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft.Json;
using WebSocketSharp;
using WebSocketSharp.Server;

namespace BarnardTech.WebSocketComms
{
    internal class WebSocketListener : WebSocketBehavior, IDisposable
    {
        object _commandStructure;
        ICommsServer _server;
        Thread sendingThread;
        Timer sendTimer;
        AutoResetEvent sendSignal;
        ConcurrentQueue<TCPCommand> messageQueue;
        bool running = true;
        JsonSerializer serializer;
        Func<TCPCommand, TCPCommand, bool> _checkMessage;
        bool _echo = true;

        public WebSocketListener(object commandStructure, ICommsServer server, bool echo, Func<TCPCommand, TCPCommand, bool> checkMessage)
        {
            _echo = echo;
            _checkMessage = checkMessage;
            _commandStructure = commandStructure;
            _server = server;
            messageQueue = new ConcurrentQueue<TCPCommand>();
            sendSignal = new AutoResetEvent(false);
            serializer = new JsonSerializer();

            sendingThread = new Thread(() =>
            {
                while (running)
                {
                    while (messageQueue.Count > 0)
                    {
                        TCPCommand msg;
                        while (!messageQueue.TryDequeue(out msg))
                        {
                            Thread.Sleep(1);
                        }

                        StringWriter sw = new StringWriter();
                        serializer.Serialize(sw, msg);
                        Send(sw.ToString());
                        sw.Dispose();
                    }
                    sendSignal.WaitOne();
                }
            });
            sendingThread.Start();
        }

        void SendElapsed(object state)
        {
            while (messageQueue.Count > 0)
            {
                TCPCommand msg;
                while (!messageQueue.TryDequeue(out msg))
                {
                    Thread.Sleep(1);
                }

                if (_checkMessage != null && messageQueue.Count > 0)
                {
                    // 'checkMessage' allows us to compare the current message and the next one in the queue, to see if we should bother sending the current message
                    TCPCommand peekMessage;
                    while (!messageQueue.TryPeek(out peekMessage)) ;
                    while (!_checkMessage(msg, peekMessage))
                    {
                        // checkMessage returned false, so we need to skip the current message, move on and then run the check again.
                        Console.WriteLine("Skipping " + msg.name);
                        while (!messageQueue.TryDequeue(out msg)) ;
                        if (messageQueue.Count > 0)
                        {
                            while (!messageQueue.TryPeek(out peekMessage)) ;
                        }
                        else
                        {
                            // at end of message queue, so we can't run the check again
                            break;
                        }
                    }
                }

                StringWriter sw = new StringWriter();
                serializer.Serialize(sw, msg);
                Send(sw.ToString());
                sw.Dispose();
            }

            sendTimer = new Timer(SendElapsed, null, 100, Timeout.Infinite);
        }

        ~WebSocketListener()
        {
            running = false;
            sendSignal.Set();
        }

        protected override void OnMessage(MessageEventArgs e)
        {
            var msg = e.Data;
            if (_echo)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine(e.Data.Length < Console.WindowWidth ? e.Data : e.Data.Substring(0, Console.WindowWidth - 1));
                Console.ResetColor();
            }
            TCPCommand incomingCommand;
            JsonSerializer serializer = new JsonSerializer();
            using (JsonTextReader reader = new JsonTextReader(new StringReader(e.Data)))
            {
                incomingCommand = serializer.Deserialize<TCPCommand>(reader);
            }
            TCPCommand response = _server.executeCommand(incomingCommand);

            StringWriter sw = new StringWriter();
            serializer.Serialize(sw, response);

            Send(sw.ToString());
        }

        public void SendMessage(TCPCommand message)
        {
            messageQueue.Enqueue(message);
            sendSignal.Set();
            /*JsonSerializer serializer = new JsonSerializer();
            StringWriter sw = new StringWriter();
            serializer.Serialize(sw, message);
            Send(sw.ToString());
            sw.Dispose();*/
        }

        #region IDisposable Support
        private bool disposedValue = false; // To detect redundant calls

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    running = false;
                    sendSignal.Set();
                }

                // TODO: free unmanaged resources (unmanaged objects) and override a finalizer below.
                // TODO: set large fields to null.

                disposedValue = true;
            }
        }

        // TODO: override a finalizer only if Dispose(bool disposing) above has code to free unmanaged resources.
        // ~WebSocketListener() {
        //   // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
        //   Dispose(false);
        // }

        // This code added to correctly implement the disposable pattern.
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
            // TODO: uncomment the following line if the finalizer is overridden above.
            // GC.SuppressFinalize(this);
        }
        #endregion
    }
}
