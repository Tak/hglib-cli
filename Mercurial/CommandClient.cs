using System;
using System.IO;
using System.Xml;
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
		public IDictionary<string,string> Configuration {
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
		
		public CommandClient (string path, string encoding, IDictionary<string,string> configs)
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
		
		public static void Initialize (string destination)
		{
			using (var client = new CommandClient (null, null, null)) {
				client.InitializeInternal (destination);
			}
		}
		
		internal void InitializeInternal (string destination)
		{
			ThrowOnFail (GetCommandOutput (new[]{ "init", destination }, null), 0, "Error initializing repository");
		}
		
		public static void Clone (string source, string destination)
		{
			Clone (source, destination, true, null, null, null, false, true);
		}
		
		public static void Clone (string source, string destination, bool updateWorkingCopy, string updateToRevision, string cloneToRevision, string onlyCloneBranch, bool forcePullProtocol, bool compressData)
		{
			using (var client = new CommandClient (null, null, null)) {
				client.CloneInternal (source, destination, updateWorkingCopy, updateToRevision, cloneToRevision, onlyCloneBranch, forcePullProtocol, compressData);
			}
		}

		internal void CloneInternal (string source, string destination, bool updateWorkingCopy, string updateToRevision, string cloneToRevision, string onlyCloneBranch, bool forcePullProtocol, bool compressData)
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
		
		public void Add (params string[] files)
		{
			Add (files, null, null, false, false);
		}
		
		public void Add (IEnumerable<string> files, string includePattern, string excludePattern, bool recurseSubRepositories, bool dryRun)
		{
			if (null == files) files = new List<string> ();
			var arguments = new List<string> (){ "add" };
			AddNonemptyStringArgument (arguments, includePattern, "--include");
			AddNonemptyStringArgument (arguments, excludePattern, "--exclude");
			AddArgumentIf (arguments, recurseSubRepositories, "--subrepos");
			AddArgumentIf (arguments, dryRun, "--dry-run");
			
			arguments.AddRange (files);
			
			ThrowOnFail (GetCommandOutput (arguments, null), 0, string.Format ("Error adding {0}", string.Join (" ", files.ToArray ())));
		}
		
		public IDictionary<string,Status> Status (params string[] files)
		{
			return Status (files, Mercurial.Status.Default, true, false, null, null, null, null, false);
		}
		
		public IDictionary<string,Status> Status (IEnumerable<string> files, Status onlyFilesWithThisStatus, bool showStatusPrefix, bool showCopiedSources, string fromRevision, string onlyRevision, string includePattern, string excludePattern, bool recurseSubRepositories)
		{
			var arguments = new List<string> (){ "status" };
			
			if (Mercurial.Status.Default != onlyFilesWithThisStatus) {
				arguments.Add (ArgumentForStatus (onlyFilesWithThisStatus));
			}
			AddArgumentIf (arguments, !showStatusPrefix, "--no-status");
			AddArgumentIf (arguments, showCopiedSources, "--copies");
			AddNonemptyStringArgument (arguments, fromRevision, "--rev");
			AddNonemptyStringArgument (arguments, onlyRevision, "--change");
			AddNonemptyStringArgument (arguments, includePattern, "--include");
			AddNonemptyStringArgument (arguments, excludePattern, "--exclude");
			AddArgumentIf (arguments, recurseSubRepositories, "--subrepos");
			
			if (null != files)
				arguments.AddRange (files);
			
			CommandResult result = GetCommandOutput (arguments, null);
			ThrowOnFail (result, 0, "Error retrieving status");
			
			return result.Output.Split (new[]{"\n"}, StringSplitOptions.RemoveEmptyEntries).Aggregate (new Dictionary<string,Status> (), (dict,line) => {
				if (2 < line.Length) {
					dict [line.Substring (2)] = ParseStatus (line.Substring (0, 1));
				}
				return dict;
			},
				dict => dict
			);
		}
		
		public void Commit (string message, params string[] files)
		{
			Commit (message, files, false, false, null, null, null, DateTime.MinValue, null);
		}
		
		public void Commit (string message, IEnumerable<string> files, bool addAndRemoveUnknowns, bool closeBranch, string includePattern, string excludePattern, string messageLog, DateTime date, string user)
		{
			var arguments = new List<string> (){ "commit" };
			AddNonemptyStringArgument (arguments, message, "--message");
			AddArgumentIf (arguments, addAndRemoveUnknowns, "--addremove");
			AddArgumentIf (arguments, closeBranch, "--close-branch");
			AddNonemptyStringArgument (arguments, includePattern, "--include");
			AddNonemptyStringArgument (arguments, excludePattern, "--exclude");
			AddNonemptyStringArgument (arguments, messageLog, "--logfile");
			AddNonemptyStringArgument (arguments, user, "--user");
			AddFormattedDateArgument (arguments, date, "--date");
			
			CommandResult result = GetCommandOutput (arguments, null);
			if (1 != result.Result && 0 != result.Result) {
				ThrowOnFail (result, 0, "Error committing");
			}
		}
		
		public IList<Revision> Log (string revisionRange, params string[] files)
		{
			return Log (revisionRange, files, false, false, DateTime.MinValue, DateTime.MinValue, false, null, false, false, false, null, null, null, 0, null, null);
		}
		
		public IList<Revision> Log (string revisionRange, IEnumerable<string> files, bool followAcrossCopy, bool followFirstMergeParent, DateTime fromDate, DateTime toDate, bool showCopiedFiles, string searchText, bool showRemoves, bool onlyMerges, bool excludeMerges, string user, string branch, string pruneRevisions, int limit, string includePattern, string excludePattern)
		{
			var arguments = new List<string> (){ "log", "--style", "xml" };
			AddNonemptyStringArgument (arguments, revisionRange, "--rev");
			AddArgumentIf (arguments, followAcrossCopy, "--follow");
			AddArgumentIf (arguments, followFirstMergeParent, "--follow-first");
			AddArgumentIf (arguments, showCopiedFiles, "--copies");
			AddNonemptyStringArgument (arguments, searchText, "--keyword");
			AddArgumentIf (arguments, showRemoves, "--removed");
			AddArgumentIf (arguments, onlyMerges, "--only-merges");
			AddArgumentIf (arguments, excludeMerges, "--no-merges");
			AddNonemptyStringArgument (arguments, user, "--user");
			AddNonemptyStringArgument (arguments, branch, "--branch");
			AddNonemptyStringArgument (arguments, pruneRevisions, "--prune");
			AddNonemptyStringArgument (arguments, includePattern, "--include");
			AddNonemptyStringArgument (arguments, excludePattern, "--exclude");
			if (0 < limit) {
				arguments.Add ("--limit");
				arguments.Add (limit.ToString ());
			}
			if (DateTime.MinValue != fromDate && DateTime.MinValue != toDate) {
				arguments.Add (string.Format ("{0} to {1}",
				                              fromDate.ToString ("yyyy-MM-dd HH:mm:ss"),
				                              toDate.ToString ("yyyy-MM-dd HH:mm:ss")));
			}
			if (null != files)
				arguments.AddRange (files);
			
			CommandResult result = GetCommandOutput (arguments, null);
			ThrowOnFail (result, 0, "Error getting log");
			
			XmlDocument document = new XmlDocument ();
			
			try {
				document.LoadXml (result.Output);
			} catch (XmlException ex) {
				throw new CommandException ("Error getting log", ex);
			}
			
			var revisions = new List<Revision> ();
			foreach (XmlNode node in document.SelectNodes ("/log/logentry")) {
				revisions.Add (new Revision (node));
			}
			
			return revisions;
		}
		
		public string Annotate (string revision, params string[] files)
		{
			return Annotate (revision, files, true, false, true, false, false, true, false, false, null, null);
		}
		
		public string Annotate (string revision, IEnumerable<string> files, bool followCopies, bool annotateBinaries, bool showAuthor, bool showFilename, bool showDate, bool showRevision, bool showChangeset, bool showLine, string includePattern, string excludePattern)
		{
			List<string > arguments = new List<string> (){ "annotate" };
			
			AddNonemptyStringArgument (arguments, revision, "--rev");
			AddArgumentIf (arguments, !followCopies, "--no-follow");
			AddArgumentIf (arguments, annotateBinaries, "--text");
			AddArgumentIf (arguments, showAuthor, "--user");
			AddArgumentIf (arguments, showFilename, "--file");
			AddArgumentIf (arguments, showDate, "--date");
			AddArgumentIf (arguments, showRevision, "--number");
			AddArgumentIf (arguments, showChangeset, "--changeset");
			AddArgumentIf (arguments, showLine, "--line-number");
			AddNonemptyStringArgument (arguments, includePattern, "--include");
			AddNonemptyStringArgument (arguments, excludePattern, "--exclude");
			
			if (null != files)
				arguments.AddRange (files);
			
			CommandResult result = GetCommandOutput (arguments, null);
			ThrowOnFail (result, 0, "Error annotating");
			
			return result.Output;
		}
		
		public string Diff (string revision, params string[] files)
		{
			return Diff (revision, files, null, false, false, true, false, false, false, false, false, 0, null, null, false);
		}
		
		public string Diff (string revision, IEnumerable<string> files, string changeset, bool diffBinaries, bool useGitFormat, bool showDates, bool showFunctionNames, bool reverse, bool ignoreWhitespace, bool ignoreWhitespaceOnlyChanges, bool ignoreBlankLines, int contextLines, string includePattern, string excludePattern, bool recurseSubRepositories)
		{
			var arguments = new List<string> (){ "diff" };
			AddNonemptyStringArgument (arguments, revision, "--rev");
			AddNonemptyStringArgument (arguments, changeset, "--change");
			AddArgumentIf (arguments, diffBinaries, "--text");
			AddArgumentIf (arguments, useGitFormat, "--git");
			AddArgumentIf (arguments, !showDates, "--nodates");
			AddArgumentIf (arguments, showFunctionNames, "--show-function");
			AddArgumentIf (arguments, reverse, "--reverse");
			AddArgumentIf (arguments, ignoreWhitespace, "--ignore-all-space");
			AddArgumentIf (arguments, ignoreWhitespaceOnlyChanges, "--ignore-space-change");
			AddArgumentIf (arguments, ignoreBlankLines, "--ignore-blank-lines");
			AddNonemptyStringArgument (arguments, includePattern, "--include");
			AddNonemptyStringArgument (arguments, excludePattern, "--exclude");
			AddArgumentIf (arguments, recurseSubRepositories, "--subrepos");
			if (0 < contextLines) {
				arguments.Add ("--unified");
				arguments.Add (contextLines.ToString ());
			}
			
			if (null != files) arguments.AddRange (files);
			
			CommandResult result = GetCommandOutput (arguments, null);
			ThrowOnFail (result, 0, "Error getting diff");
			
			return result.Output;
		}
		
		public string Export (params string[] revisions)
		{
			return Export (revisions, null, false, false, false, true);
		}
		
		public string Export (IEnumerable<string> revisions, string outputFile, bool switchParent, bool diffBinaries, bool useGitFormat, bool showDates)
		{
			if (null == revisions || 0 == revisions.Count ())
				throw new ArgumentException ("Revision list cannot be empty", "revisions");
			
			var arguments = new List<string> (){ "export" };
			AddNonemptyStringArgument (arguments, outputFile, "--output");
			AddArgumentIf (arguments, switchParent, "--switch-parent");
			AddArgumentIf (arguments, diffBinaries, "--text");
			AddArgumentIf (arguments, useGitFormat, "--git");
			AddArgumentIf (arguments, !showDates, "--nodates");
			arguments.AddRange (revisions);
			
			CommandResult result = GetCommandOutput (arguments, null);
			ThrowOnFail (result, 0, string.Format ("Error exporting {0}", string.Join (",", revisions.ToArray ())));
			
			return result.Output;
		}
		
		public void Forget (params string[] files)
		{
			Forget (files, null, null);
		}
		
		public void Forget (IEnumerable<string> files, string includePattern, string excludePattern)
		{
			if (null == files || 0 == files.Count ())
				throw new ArgumentException ("File list cannot be empty", "files");
				
			var arguments = new List<string> (){ "forget" };
			AddNonemptyStringArgument (arguments, includePattern, "--include");
			AddNonemptyStringArgument (arguments, excludePattern, "--exclude");
			arguments.AddRange (files);
			
			ThrowOnFail (GetCommandOutput (arguments, null), 0, string.Format ("Error forgetting {0}", string.Join (",", files.ToArray ())));
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
		                       IDictionary<CommandChannel,Stream> outputs,
		                       IDictionary<CommandChannel,Func<uint,byte[]>> inputs)
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
		                                       IDictionary<CommandChannel,Func<uint,byte[]>> inputs)
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
		
		static void AddFormattedDateArgument (IList<string> arguments, DateTime date, string datePrefix)
		{
			if (DateTime.MinValue != date) {
				arguments.Add (datePrefix);
				arguments.Add (date.ToString ("yyyy-MM-dd HH:mm:ss"));
			}
		}

		CommandResult ThrowOnFail (CommandResult result, int expectedResult, string failureMessage)
		{
			if (expectedResult != result.Result) {
				throw new CommandException (failureMessage, result);
			}
			return result;
		}
		
		static string ArgumentForStatus (Mercurial.Status status)
		{
			switch (status) {
			case Mercurial.Status.Added:
				return "--added";
			case Mercurial.Status.Clean:
				return "--clean";
			case Mercurial.Status.Ignored:
				return "--ignored";
			case Mercurial.Status.Modified:
				return "--modified";
			case Mercurial.Status.Removed:
				return "--removed";
			case Mercurial.Status.Unknown:
				return "--unknown";
			case Mercurial.Status.Missing:
				return "--deleted";
			case Mercurial.Status.All:
				return "--all";
			default:
				return string.Empty;
			}
		}
		
		static Mercurial.Status ParseStatus (string input)
		{
			switch (input) {
			case "M":
				return Mercurial.Status.Modified;
			case "A":
				return Mercurial.Status.Added;
			case "R":
				return Mercurial.Status.Removed;
			case "C":
				return Mercurial.Status.Clean;
			case "!":
				return Mercurial.Status.Missing;
			case "?":
				return Mercurial.Status.Unknown;
			case "I":
				return Mercurial.Status.Ignored;
			case " ":
				return Mercurial.Status.Origin;
			default:
				return Mercurial.Status.Clean; // ?
			}
		}
		
		#endregion
	}
}

