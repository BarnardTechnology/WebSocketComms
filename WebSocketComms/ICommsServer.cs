using System;
using System.Collections.Generic;
using System.Text;

namespace BarnardTech.WebSocketComms
{
    public interface ICommsServer
    {
        TCPCommand executeCommand(TCPCommand cmd);
    }
}