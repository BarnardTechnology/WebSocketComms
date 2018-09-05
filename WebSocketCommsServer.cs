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

		WebSocketServer _wssv = null;

        public CommsServer(string linkName, int port = 80, bool consoleEcho = false, bool loopbackOnly = false)
        {
            webSocketListeners = new List<WebSocketListener>();
            _linkName = linkName;
            _token = new CancellationTokenSource();
            if (loopbackOnly)
            {
                _wssv = new WebSocketServer(IPAddress.Loopback, port);
            }
            else
            {
                _wssv = new WebSocketServer(port);
            }
            _wssv.Start();
        }

        public void AddRoute(string prefix, object commandStructure, Func<TCPCommand, TCPCommand, bool> checkMessage = null)
        {
            if (!prefix.StartsWith("/"))
            {
                prefix = "/" + prefix;
            }

            _commandStructure = commandStructure;
            _prefix = prefix;

            _wssv.AddWebSocketService<WebSocketListener>(prefix, () =>
            {
                //if (consoleEcho)
                //{
                //    Console.ForegroundColor = ConsoleColor.Yellow;
                //    Console.WriteLine("WebSocketService created at " + prefix + ".");
                //    Console.ResetColor();
                //}
                WebSocketListener gl = new WebSocketListener(commandStructure, this, false, checkMessage);
                webSocketListeners.Add(gl);
                return gl;
            });
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