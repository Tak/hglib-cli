using System;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.Collections.Generic;

namespace Mercurial
{
	public class CommandClient: IDisposable
	{
		static readonly string MercurialPath = "/home/levi/Code/mercurial/hg";
		static readonly string MercurialEncodingKey = "HGENCODING";
		static readonly int MercurialHeaderLength = 5;
		
		Process commandServer = null;
		
		public string Encoding { get; private set; }
		public IEnumerable<string> Capabilities { get; private set; }
		
		public CommandClient (string path, string encoding, Dictionary<string,string> configs)
		{
			var arguments = new StringBuilder ("serve --cmdserver pipe ");
			
			if (!string.IsNullOrEmpty (path)) {
				arguments.AppendFormat ("-R path");
			}
			
			if (null != configs) {
				// build config string in key=value format
				arguments.AppendFormat ("--config {0}", 
					configs.Aggregate (new StringBuilder (),
						(accumulator, pair) => accumulator.AppendFormat ("{0}={1},", pair.Key, pair.Value),
						accumulator => accumulator.ToString ()
				));
			}
			
			ProcessStartInfo commandServerInfo = new ProcessStartInfo (MercurialPath, arguments.ToString ());
			if (null != encoding) {
				commandServerInfo.EnvironmentVariables [MercurialEncodingKey] = encoding;
			}
			commandServerInfo.RedirectStandardInput =
			commandServerInfo.RedirectStandardOutput = 
			commandServerInfo.RedirectStandardError = true;
			commandServerInfo.UseShellExecute = false;
			
			try {
				commandServer = Process.Start (commandServerInfo);
			} catch (Exception ex) {
				throw new ServerException ("Error launching mercurial command server", ex);
			}
			
			Handshake ();
		}
		
		public void Handshake ()
		{
			CommandMessage handshake = ReadMessage ();
			Dictionary<string,string > headers = ParseHeaders (handshake.Message);
			
			if (!headers.ContainsKey ("encoding") || !headers.ContainsKey ("capabilities")) {
				throw new ServerException ("Error handshaking: expected 'encoding' and 'capabilities' fields");
			}
			
			Encoding = headers ["encoding"];
			Capabilities = headers ["capabilities"].Split (new[]{" "}, StringSplitOptions.RemoveEmptyEntries);
		}

		public CommandMessage ReadMessage ()
		{
			byte[] header = new byte[MercurialHeaderLength];
			int bytesRead = 0;
			
			try {
				bytesRead = commandServer.StandardOutput.BaseStream.Read (header, 0, MercurialHeaderLength);
			} catch (Exception ex) {
				throw new ServerException ("Error reading from command server", ex);
			}
			
			if (MercurialHeaderLength != bytesRead) {
				throw new ServerException (string.Format ("Received malformed header from command server: {0} bytes", bytesRead));
			}
			
			long messageLength = (long)ReadUint (header, 1);
			byte[] messageBuffer = new byte[messageLength];
			
			try {
				if (messageLength > int.MaxValue) {
					// .NET hates uints
					int firstPart = (int)(messageLength / 2);
					int secondPart = (int)(messageLength - firstPart);
				
					commandServer.StandardOutput.BaseStream.Read (messageBuffer, 0, firstPart);
					commandServer.StandardOutput.BaseStream.Read (messageBuffer, firstPart, secondPart);
				} else {
					commandServer.StandardOutput.BaseStream.Read (messageBuffer, 0, (int)messageLength);
				}
			} catch (Exception ex) {
				throw new ServerException ("Error reading from command server", ex);
			}
				
			CommandMessage message = new CommandMessage (CommandChannelFromFirstByte (header), messageBuffer);
			Console.WriteLine ("READ: {0} {1}", message, message.Message);
			return message;
		}

		#region IDisposable implementation
		public void Dispose ()
		{
			commandServer.Close ();
		}
		#endregion		
		
		#region Utility
		
		public static uint ReadUint (byte[] buffer, int offset)
		{
			if (null == buffer) throw new ArgumentNullException ("buffer");
			if (buffer.Length < offset + 4) throw new ArgumentOutOfRangeException ("offset");
			
			byte[] privateBuffer = new byte[4];
			Array.Copy (buffer, offset, privateBuffer, 0, 4);
			if (BitConverter.IsLittleEndian) Array.Reverse (privateBuffer);
			return BitConverter.ToUInt32 (privateBuffer, 0);
		}
		
		public static CommandChannel CommandChannelFromFirstByte (byte[] header)
		{
			char[] identifier = ASCIIEncoding.ASCII.GetChars (header, 0, 1);
			
			switch (identifier [0]) {
			case 'I':
				return CommandChannel.Input;
			case 'L':
				return CommandChannel.Line;
			case 'o':
				return CommandChannel.Output;
			case 'e':
				return CommandChannel.Error;
			case 'r':
				return CommandChannel.Result;
			case 'd':
				return CommandChannel.Debug;
			default:
				throw new ArgumentException (string.Format ("Invalid channel identifier: {0}", identifier[0]), "header");
			}
		}
		
		public static byte CommandChannelToByte (CommandChannel channel)
		{
			string identifier;
			
			switch (channel) {
			case CommandChannel.Debug:
				identifier = "d";
				break;
			case CommandChannel.Error:
				identifier = "e";
				break;
			case CommandChannel.Input:
				identifier = "I";
				break;
			case CommandChannel.Line:
				identifier = "L";
				break;
			case CommandChannel.Output:
				identifier = "o";
				break;
			case CommandChannel.Result:
				identifier = "r";
				break;
			default:
				identifier = string.Empty;
				break;
			}
			byte[] bytes = ASCIIEncoding.ASCII.GetBytes (identifier);
			return bytes[0];
		}
		
		static Dictionary<string,string> ParseHeaders (string headerString)
		{
			string[] headerDelimiters = new string[]{ ": " };
			Dictionary<string,string > headers = headerString.Split ('\n')
				.Aggregate (new Dictionary<string,string> (),
					(dict,line) => {
				var tokens = line.Split (headerDelimiters, 2, StringSplitOptions.None);
				dict [tokens [0]] = tokens [1];
				return dict;
			},
					dict => dict
				);
			return headers;
		}
		
		#endregion
	}
}

