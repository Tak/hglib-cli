using System;
using System.IO;
using System.Net;
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
		public Dictionary<string,string> Configuration {
			get {
				if (null != _configuration)
					return _configuration;
				
				CommandResult result = GetCommandOutput (new[]{"showconfig"}, null);
				if (0 == result.Result) {
					return _configuration = ParseDictionary (result.Output, new[]{"="});
				}
				return null;
			}
		}
		Dictionary<string,string> _configuration;
		
		public string Root {
			get {
				if (null != _root) return _root;
				return _root = GetCommandOutput (new[]{"root"}, null).Output.TrimEnd ();
			}
		}
		string _root;
		
		public CommandClient (string path, string encoding, Dictionary<string,string> configs)
		{
			var arguments = new StringBuilder ("serve --cmdserver pipe ");
			
			if (!string.IsNullOrEmpty (path)) {
				arguments.AppendFormat ("-R {0} ", path);
			}
			
			if (null != configs) {
				// build config string in key=value format
				arguments.AppendFormat ("--config {0} ", 
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
				// Console.WriteLine ("Launching command server with: {0} {1}", MercurialPath, arguments.ToString ());
				commandServer = Process.Start (commandServerInfo);
			} catch (Exception ex) {
				throw new ServerException ("Error launching mercurial command server", ex);
			}
			
			Handshake ();
		}
		
		public void Initialize (string destination)
		{
			ThrowOnFail (GetCommandOutput (new[]{ "init", destination }, null), 0, "Error initializing repository");
		}
		
		public void Clone (string source, string destination)
		{
			Clone (source, destination, true, null, null, null, false, true);
		}
		
		public void Clone (string source, string destination, bool updateWorkingCopy, string updateToRevision, string cloneToRevision, string onlyCloneBranch, bool forcePullProtocol, bool compressData)
		{
			if (string.IsNullOrEmpty (source)) 
				throw new ArgumentException ("Source must not be empty.", "source");
			
			var arguments = new List<string> (){ "clone" };
			AddArgumentIf (arguments, !updateWorkingCopy, "--noupdate");
			AddArgumentIf (arguments, forcePullProtocol, "--pull");
			AddArgumentIf (arguments, !compressData, "--uncompressed");
			
			AddNonemptyStringArgument (arguments, updateToRevision, "--updaterev");
			AddNonemptyStringArgument (arguments, cloneToRevision, "--rev");
			AddNonemptyStringArgument (arguments, onlyCloneBranch, "--branch");
			
			arguments.Add (source);
			AddArgumentIf (arguments, !string.IsNullOrEmpty (destination), destination);
			
			ThrowOnFail (GetCommandOutput (arguments, null), 0, string.Format ("Error cloning to {0}", source));
		}
		
		#region Plumbing
		
		public void Handshake ()
		{
			CommandMessage handshake = ReadMessage ();
			Dictionary<string,string > headers = ParseDictionary (handshake.Message, new[]{": "});
			
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
			
			CommandChannel channel = CommandChannelFromFirstByte (header);
			long messageLength = (long)ReadUint (header, 1);
			
			if (CommandChannel.Input == channel || CommandChannel.Line == channel)
				return new CommandMessage (channel, messageLength.ToString ());
			
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
			// Console.WriteLine ("READ: {0} {1}", message, message.Message);
			return message;
		}
		
		public int RunCommand (IList<string> command,
		                       Dictionary<CommandChannel,Stream> outputs,
		                       Dictionary<CommandChannel,Func<uint,byte[]>> inputs)
		{
			if (null == command || 0 == command.Count)
				throw new ArgumentException ("Command must not be empty", "command");
			
			byte[] commandBuffer = UTF8Encoding.UTF8.GetBytes ("runcommand\n");
			byte[] argumentBuffer;
			
			argumentBuffer = command.Aggregate (new List<byte> (), (bytes,arg) => {
				bytes.AddRange (UTF8Encoding.UTF8.GetBytes (arg));
				bytes.Add (0);
				return bytes;
			},
				bytes => {
				bytes.RemoveAt (bytes.Count - 1);
				return bytes.ToArray ();
			}
			);
			
			byte[] lengthBuffer = BitConverter.GetBytes (IPAddress.HostToNetworkOrder (argumentBuffer.Length));
			
			commandServer.StandardInput.BaseStream.Write (commandBuffer, 0, commandBuffer.Length);
			commandServer.StandardInput.BaseStream.Write (lengthBuffer, 0, lengthBuffer.Length);
			commandServer.StandardInput.BaseStream.Write (argumentBuffer, 0, argumentBuffer.Length);
			commandServer.StandardInput.BaseStream.Flush ();
			
			while (true) {
				CommandMessage message = ReadMessage ();
				if (CommandChannel.Result == message.Channel)
					return ReadInt (message.Buffer, 0);
					
				if (inputs != null && inputs.ContainsKey (message.Channel)) {
					byte[] sendBuffer = inputs [message.Channel] (ReadUint (message.Buffer, 0));
					if (null == sendBuffer || 0 == sendBuffer.LongLength) {
					} else {
					}
				}
				if (outputs != null && outputs.ContainsKey (message.Channel)) {
					if (message.Buffer.Length > int.MaxValue) {
						// .NET hates uints
						int firstPart = message.Buffer.Length / 2;
						int secondPart = message.Buffer.Length - firstPart;
						outputs [message.Channel].Write (message.Buffer, 0, firstPart);
						outputs [message.Channel].Write (message.Buffer, firstPart, secondPart);
					} else {
						outputs [message.Channel].Write (message.Buffer, 0, message.Buffer.Length);
					}
				}
			}
		}
		
		public CommandResult GetCommandOutput (IList<string> command,
		                                       Dictionary<CommandChannel,Func<uint,byte[]>> inputs)
		{
			MemoryStream output = new MemoryStream ();
			MemoryStream error = new MemoryStream ();
			var outputs = new Dictionary<CommandChannel,Stream> () {
				{ CommandChannel.Output, output },
				{ CommandChannel.Error, error },
			};
			
			int result = RunCommand (command, outputs, inputs);
			return new CommandResult (UTF8Encoding.UTF8.GetString (output.GetBuffer (), 0, (int)output.Length),
			                          UTF8Encoding.UTF8.GetString (error.GetBuffer (), 0, (int)error.Length),
			                          result);
		}
		
		public void Close ()
		{
			if (null != commandServer) 
				commandServer.Close ();
			commandServer = null;
		}

		#region IDisposable implementation
		public void Dispose ()
		{
			Close ();
		}
		#endregion		
		
		#endregion
		
		#region Utility
		
		public static int ReadInt (byte[] buffer, int offset)
		{
			if (null == buffer) throw new ArgumentNullException ("buffer");
			if (buffer.Length < offset + 4) throw new ArgumentOutOfRangeException ("offset");
			
			return IPAddress.NetworkToHostOrder (BitConverter.ToInt32 (buffer, offset));
		}
		
		public static uint ReadUint (byte[] buffer, int offset)
		{
			if (null == buffer)
				throw new ArgumentNullException ("buffer");
			if (buffer.Length < offset + 4)
				throw new ArgumentOutOfRangeException ("offset");
			
			return (uint)IPAddress.NetworkToHostOrder (BitConverter.ToInt32 (buffer, offset));
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
		
		static Dictionary<string,string> ParseDictionary (string input, string[] delimiters)
		{
			Dictionary<string,string > headers = input.Split ('\n')
				.Aggregate (new Dictionary<string,string> (),
					(dict,line) => {
				var tokens = line.Split (delimiters, 2, StringSplitOptions.None);
				if (2 == tokens.Count ())
					dict [tokens [0]] = tokens [1];
				return dict;
			},
					dict => dict
				);
			return headers;
		}
		

		static void AddArgumentIf (IList<string> arguments, bool condition, string argument)
		{
			if (condition) arguments.Add (argument);
		}
		

		static void AddNonemptyStringArgument (IList<string> arguments, string argument, string argumentPrefix)
		{
			if (!string.IsNullOrEmpty (argument)) {
				arguments.Add (argumentPrefix);
				arguments.Add (argument);
			}
		}

		CommandResult ThrowOnFail (CommandResult result, int expectedResult, string failureMessage)
		{
			if (expectedResult != result.Result) {
				throw new CommandException (failureMessage, result);
			}
			return result;
		}
		#endregion
	}
}

