using System;
using System.Text;

namespace Mercurial
{
	public class CommandMessage
	{
		public CommandChannel Channel { get; private set; }
		public byte[] Buffer { get; private set; }

		public string Message {
			get {
				if (null != _message) return _message;
				return _message = new string (UTF8Encoding.UTF8.GetChars (Buffer));
			}
		}
		
		string _message;
		
		public CommandMessage (CommandChannel channel, byte[] buffer)
		{
			Channel = channel;
			Buffer = buffer;
		}
	}
}

