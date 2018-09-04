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

namespace WebSocketComms
{
	public class CommsServer
	{
		object _commandStructure;
		string _linkName;
        string _prefix;

		public bool enabled = true;
		CancellationTokenSource _token;
		List<WebSocketListener> webSocketListeners;

		static WebSocketServer wssv = null;

        /// <summary>
        /// Start running the WebSocket CommsServer
        /// </summary>
        /// <param name="linkName">The name to use for this CommsServer.</param>
        /// <param name="prefix">The websocket address prefix.</param>
        /// <param name="commandStructure">An object containing commands that should be exposed to the client.</param>
        /// <param name="port">The port for listening to communications.</param>
        /// <param name="checkMessage">(Experimental) A function to call prior to processing the next message - supplies both the current TCPCommand that is about to be run, and the next TCPCommand that will run after it. Returning 'false' within this function will prevent the current TCPCommand from running - the idea being, if the CommsServer is being bombarded with identical messages, you can elect to drop them rather than deal with the processing.</param>
        /// <param name="consoleEcho">Echoes comms output to the console.</param>
        /// <param name="loopbackOnly">If set to 'true', the CommsServer will only bind to the loopback address.</param>
		public CommsServer(string linkName, string prefix, object commandStructure, int port = 80, Func<TCPCommand, TCPCommand, bool> checkMessage = null, bool consoleEcho = false, bool loopbackOnly = false)
		{
			if (!prefix.StartsWith("/"))
			{
				prefix = "/" + prefix;
			}

			webSocketListeners = new List<WebSocketListener>();
			_linkName = linkName;
			_token = new CancellationTokenSource();
			_commandStructure = commandStructure;

			bool needsStarting = false;
			if (wssv == null)
			{
				if (loopbackOnly)
				{
					wssv = new WebSocketServer(IPAddress.Loopback, port);
				}
				else
				{
					wssv = new WebSocketServer(port);
				}
				needsStarting = true;
			}

			wssv.AddWebSocketService<WebSocketListener>(prefix, () =>
			{
                if (consoleEcho)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("WebSocketService created at " + prefix + ".");
                    Console.ResetColor();
                }
				WebSocketListener gl = new WebSocketListener(commandStructure, this, consoleEcho, checkMessage);
				webSocketListeners.Add(gl);
				return gl;
			});

			if (needsStarting)
			{
				wssv.Start();
			}
		}

		public void SendMessage(TCPCommand message)
		{
			foreach (WebSocketListener l in webSocketListeners)
			{
				if (l.State == WebSocketState.Open)
				{
					l.SendMessage(message);
				}
			}
		}

		public class WebSocketListener : WebSocketBehavior
		{
			object _commandStructure;
			CommsServer _server;
			Thread sendingThread;
			Timer sendTimer;
			AutoResetEvent sendSignal;
			ConcurrentQueue<TCPCommand> messageQueue;
			bool running = true;
			JsonSerializer serializer;
			Func<TCPCommand, TCPCommand, bool> _checkMessage;
			bool _echo = true;

			public WebSocketListener(object commandStructure, CommsServer server, bool echo, Func<TCPCommand, TCPCommand, bool> checkMessage)
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

							/*if (checkMessage != null && messageQueue.Count > 0)
                            {
                                // 'checkMessage' allows us to compare the current message and the next one in the queue, to see if we should bother sending the current message
                                TCPCommand peekMessage;
                                while (!messageQueue.TryPeek(out peekMessage)) ;
                                while (!checkMessage(msg, peekMessage))
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
                            }*/

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
		}


		/*private class ClientProcObj
        {
            public Thread CommsThread;
            public TcpClient Client;
            public ClientProcObj(Thread commsThread, TcpClient client)
            {
                CommsThread = commsThread;
                Client = client;
            }
        }*/

		/*private void StartServerTask()
        {
            _serverTask = Task.Factory.StartNew(() =>
            {
                listener = new TcpListener(IPAddress.Any, _listenPort);
                listener.Start();
                while(enabled)
                {
                    TcpClient client = null;
                    try
                    {
                        client = listener.AcceptTcpClient();
                        Thread CommsThread = new Thread(new ParameterizedThreadStart(ClientProcess));
                        _commsThreads.Add(CommsThread);
                        CommsThread.Start(new ClientProcObj(CommsThread, client));
                    }
                    catch(SocketException ex)
                    {
                        if(enabled)
                        {
                            throw ex;
                        }
                    }
                }
            }, _token.Token);
        }*/

		/*private void ClientProcess(object parameters)
        {
            DateTime LastRead = DateTime.Now;
            ClientProcObj o = (ClientProcObj)parameters;
            //clients.Add(o);
            
            NetworkStream nStream = o.Client.GetStream();
            StreamWriter writer = new StreamWriter(o.Client.GetStream());
            //clientWriters.Add(writer);
            //Listening = true;
            TCPCommand obj = ReadObject<TCPCommand>(nStream);
            try
            {
                TCPCommand retCmd = executeCommand(obj);
                if (retCmd != null)
                {
                    SendObject<TCPCommand>(writer, retCmd);
                }
                //SendObject<tcpCommand>(writer, executeCommand(obj));
                //while (obj != null)
                while (enabled)
                {
                    //Listening = true;
                    obj = ReadObject<TCPCommand>(nStream);
                    LastRead = DateTime.Now;
                    if (obj != null)
                    {
                        retCmd = executeCommand(obj);
                        if (retCmd != null)
                        {
                            SendObject<TCPCommand>(writer, retCmd);
                        }
                    }
                    else
                    {
                        // object couldn't be read?
                        SocketException sockEx = (SocketException)lastError;
                        if (sockEx.SocketErrorCode == SocketError.ConnectionReset)
                        {
                            break;
                        }
                        else
                        {
                            Exception readException = new Exception("Data from client couldn't be read. The line received was:\r\n" + lastLineRead + "\r\n\r\nLast error was:\r\n" + lastError.ToString() + "\r\n");
                            //LoggedErrors.Enqueue(readException);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                //LoggedErrors.Enqueue(ex);
            }
            try
            {
                _commsThreads.Remove(o.CommsThread);
                //clientWriters.Remove(writer);
            }
            catch (Exception) { }
            try
            {
                writer.Close();
                o.Client.Close();
            }
            catch (Exception)
            { }
            //clients.Remove(o);
            //Listening = false;
        }
        const char STX = (char)2;
        const char ETX = (char)3;
        public bool SendObject<T>(StreamWriter s, T o)
        {
            if (s != null)
            {
                try
                {
                    //s.WriteLine(JsonConvert.SerializeObject(o));
                    //string sCmd = JsonConvert.SerializeObject(o);
                    //s.Write(STX + sCmd + ETX);
                    MemoryStream ms = new MemoryStream();
                    using (Newtonsoft.Json.Bson.BsonWriter writer = new Newtonsoft.Json.Bson.BsonWriter(ms))
                    {
                        JsonSerializer serializer = new JsonSerializer();
                        serializer.Serialize(writer, o);
                    }
                    string data = Convert.ToBase64String(ms.ToArray());
                    s.Write(STX + data + ETX);
                    s.Flush();
                    return true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                    return false;
                }
            }
            else
            {
                return false;
            }
        }
        public T ReadObject<T>(NetworkStream r)
        {
            try
            {
                lastError = new Exception("No error.");
                bool gotSTX = false;
                int readByte = r.ReadByte();
                string cmd = "";
                while (readByte > -1)
                {
                    if (gotSTX)
                    {
                        if (readByte == 3)
                        {
                            break;
                        }
                        else
                        {
                            char c = (char)readByte;
                            cmd += c;
                        }
                    }
                    if (readByte == 2)
                    {
                        gotSTX = true;
                    }
                    readByte = r.ReadByte();
                }
                lastLineRead = cmd;
                if (cmd == "") return default(T);
                //byte[] data = Convert.FromBase64String(cmd);
                //MemoryStream ms = new MemoryStream(data);
                
                T obj;
                //using (JsonTextReader reader = new JsonTextReader(ms))
                using (JsonTextReader reader = new JsonTextReader(new StringReader(cmd)))
                {
                    JsonSerializer serializer = new JsonSerializer();
                    obj = serializer.Deserialize<T>(reader);
                }
                if (obj == null)
                {
                    // if JsonConvert returns null, try the .Net JavaScriptSerializer instead
                    //System.Web.Script.Serialization.JavaScriptSerializer js = new System.Web.Script.Serialization.JavaScriptSerializer();
                    //obj = js.Deserialize<T>(cmd);
                    throw new Exception("Deserializer returned null!");
                }
                return obj;
            }
            catch (SocketException socketError)
            {
                lastError = socketError;
                return default(T);
            }
            catch (IOException ioEx)
            {
                SocketException sockEx = (SocketException)ioEx.InnerException;
                if (sockEx.SocketErrorCode == SocketError.ConnectionReset)
                {
                    // connection reset
                    lastError = sockEx;
                    return default(T);
                }
                return default(T);
            }
            catch (Exception ex)
            {
                lastError = ex;
                return default(T);
            }
        }*/

		public void ShowMethods(Type type)
		{
			foreach (var method in type.GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.Instance))
			{
				var parameters = method.GetParameters();
				var parameterDescription = string.Join(", ", parameters.Select(x => x.ParameterType + " " + x.Name).ToArray());
				Console.WriteLine("{0} {1} ({2})", method.ReturnType, method.Name, parameterDescription);
			}
		}

		public MethodInfo FindMethod(string methodName, Type type)
		{
			foreach (var method in type.GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.Instance))
			{
				//var parameters = method.GetParameters();
				//var parameterDescription = string.Join(", ", parameters.Select(x => x.ParameterType + " " + x.Name).ToArray());
				//Console.WriteLine("{0} {1} ({2})", method.ReturnType, method.Name, parameterDescription);
				if (methodName == method.Name)
				{
					return method;
				}
			}
			return null;
		}

		public TCPCommand executeCommand(TCPCommand cmd)
		{
			if (cmd == null)
			{
				// null command? Can't do anything with a null command!
				return null;
			}
			else
			{
				try
				{
					MethodInfo method = FindMethod(cmd.name, _commandStructure.GetType());
					if (method == null)
					{
						//tcpCommand response = new tcpCommand("__error", null);
						//response.guid = cmd.guid;
						//return response;
						if (cmd.name == "GetName")
						{
							TCPCommand resp = new TCPCommand("__response", new List<object> { _linkName });
							resp.guid = cmd.guid;
							return resp;
						}
						return null;
					}
					else
					{
						object[] args;

						if (cmd.arguments != null)
						{
							args = cmd.arguments.ToArray();
						}
						else
						{
							args = new object[0];
						}

						int idx = 0;
						foreach (ParameterInfo param in method.GetParameters())
						{
							if (args[idx] is Newtonsoft.Json.Linq.JArray)
							{
								Newtonsoft.Json.Linq.JArray jArr = (Newtonsoft.Json.Linq.JArray)args[idx];
								args[idx] = jArr.ToObject(param.ParameterType);
							}
							else
							{
								args[idx] = Convert.ChangeType(args[idx], param.ParameterType);
							}
							idx++;
						}
						object retval = method.Invoke(_commandStructure, args);

						TCPCommand response = new TCPCommand("__response", new List<object> { retval });
						response.guid = cmd.guid;

						return response;
					}
				}
				catch (Exception ex)
				{
					Console.WriteLine(ex.ToString());
					TCPCommand response = new TCPCommand("__error", null);
					response.guid = cmd.guid;
					return response;
				}
			}
		}
	}
}