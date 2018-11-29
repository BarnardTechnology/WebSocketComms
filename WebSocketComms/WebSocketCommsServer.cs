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
	public class WebSocketCommsServer : IDisposable, ICommsServer
	{
		object _commandStructure;
		string _linkName;
        string _prefix;

		public bool enabled = true;
		CancellationTokenSource _token;
		List<WebSocketListener> webSocketListeners;

		WebSocketServer _wssv = null;

        public WebSocketCommsServer(string linkName, int port = 80, bool consoleEcho = false, bool loopbackOnly = false)
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

        #region IDisposable Support
        private bool disposedValue = false; // To detect redundant calls

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    if(_wssv != null)
                    {
                        foreach (var l in webSocketListeners)
                        {
                            l.Context.WebSocket.Close();
                            l.Dispose();
                        }
                        _wssv.Stop();
                    }
                }

                // TODO: free unmanaged resources (unmanaged objects) and override a finalizer below.
                // TODO: set large fields to null.

                disposedValue = true;
            }
        }

        // TODO: override a finalizer only if Dispose(bool disposing) above has code to free unmanaged resources.
        // ~CommsServer() {
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