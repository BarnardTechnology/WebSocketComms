using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Newtonsoft.Json;

namespace WebSocketComms
{
	public class TCPCommand
	{
		public string name;
		public DateTime TimeSent;
		public Guid guid;
		public List<object> arguments;

		[JsonIgnore]
		public Action<object> callback;

		public TCPCommand(string Name, List<object> Arguments, Action<object> Callback = null)
		{
			TimeSent = DateTime.Now;
			guid = Guid.NewGuid();
			name = Name;
			arguments = Arguments;
			callback = Callback;
		}
	}
}