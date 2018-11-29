﻿using System;
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
    public class HttpCommsServer : ICommsServer
    {
        object _commandStructure;
        string _linkName;
        string _prefix;

        public bool enabled = true;
        CancellationTokenSource _token;
        List<WebSocketListener> webSocketListeners;
        WebContentGenerator webContent;

        HttpServer _hssv = null;

        /// <summary>
        /// Start running an Http server backed with a WebSocket CommsServer. This process
        /// implements a basic web server that serves the JavaScript required by the browser
        /// in order to communicate with this WebSocket server.
        /// </summary>
        /// <param name="linkName">The name to use for this CommsServer.</param>
        /// <param name="port">The port for listening to communications.</param>
        /// <param name="consoleEcho">Echoes comms output to the console.</param>
        /// <param name="loopbackOnly">If set to 'true', the CommsServer will only bind to the loopback address.</param>
        public HttpCommsServer(string linkName, int port = 80, bool consoleEcho = false, bool loopbackOnly = false)
        {
            webContent = new WebContentGenerator(typeof(wwwroot.Content));
            webSocketListeners = new List<WebSocketListener>();
            _linkName = linkName;
            _token = new CancellationTokenSource();
            if (loopbackOnly)
            {
                _hssv = new HttpServer(IPAddress.Loopback, port);
            }
            else
            {
                _hssv = new HttpServer(port);
            }

            _hssv.OnGet += Hssv_OnGet;

            _hssv.Start();
        }

        public void AddRoute(string prefix, object commandStructure, Func<TCPCommand, TCPCommand, bool> checkMessage = null)
        {
            if (!prefix.StartsWith("/"))
            {
                prefix = "/" + prefix;
            }

            _commandStructure = commandStructure;
            _prefix = prefix;

            _hssv.AddWebSocketService<WebSocketListener>(prefix, () =>
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

        private void Hssv_OnGet(object sender, HttpRequestEventArgs e)
        {
            byte[] content = webContent.GetContent(e.Request.Url.AbsolutePath);
            e.Response.ContentType = webContent.GetMimeType(e.Request.Url.ToString());
            e.Response.ContentLength64 = content.Length;
            e.Response.StatusCode = 200;
            e.Response.StatusDescription = "OK";
            e.Response.WriteContent(content);
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