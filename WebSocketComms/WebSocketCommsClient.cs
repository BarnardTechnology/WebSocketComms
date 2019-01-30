using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Reflection;
using Newtonsoft.Json;
using WebSocketSharp;

namespace BarnardTech.WebSocketComms
{
    public class WebSocketCommsClient : IDisposable, ICommsServer
    {
        readonly string _linkName;
        readonly bool _echo;
        readonly object _commandStructure;
        WebSocket ws;

        public WebSocketCommsClient(string linkName, Uri serverUri, object commandStructure, bool consoleEcho = false, bool loopbackOnly = false)
        {
            _linkName = linkName;
            _echo = consoleEcho;
            _commandStructure = commandStructure;
            Connect(serverUri);
        }

        private void Connect(Uri serverUri)
        {
            ws = new WebSocket(serverUri.ToString());
            ws.Connect();
            ws.OnMessage += Ws_OnMessage;
        }

        private void Ws_OnMessage(object sender, MessageEventArgs e)
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
            TCPCommand response = executeCommand(incomingCommand);

            StringWriter sw = new StringWriter();
            serializer.Serialize(sw, response);

            ws.Send(sw.ToString());
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
                    // TODO: dispose managed state (managed objects).
                }

                // TODO: free unmanaged resources (unmanaged objects) and override a finalizer below.
                // TODO: set large fields to null.

                disposedValue = true;
            }
        }

        // TODO: override a finalizer only if Dispose(bool disposing) above has code to free unmanaged resources.
        // ~WebSocketCommsClient() {
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
