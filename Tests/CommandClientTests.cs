using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using NUnit.Framework;

using Mercurial;

namespace Mercurial.Tests
{
	[TestFixture()]
	public class CommandClientTests
	{
		[Test]
		public void TestConnection ()
		{
			using (new CommandClient (null, null, null)) {
			}
		}
		
		[Test]
		public void TestConfiguration ()
		{
			using (CommandClient client = new CommandClient (null, null, null)) {
				Dictionary<string,string > config = client.Configuration;
				Assert.IsNotNull (config);
				Console.WriteLine (config.Aggregate (new StringBuilder (), (s,pair) => s.AppendFormat ("{0} = {1}\n", pair.Key, pair.Value), s => s.ToString ()));
			}
		}
		
		[Test]
		public void TestInitialize ()
		{
			using (var client = new CommandClient (null, null, null)) {
				string path = GetTemporaryPath ();
				client.Initialize (path);
				Assert.That (Directory.Exists (Path.Combine (path, ".hg")), string.Format ("Repository was not created at {0}", path));
			}
		}
		
		[Test]
		public void TestRoot ()
		{
			string path = GetTemporaryPath ();
			
			using (var client = new CommandClient (null, null, null)) {
				client.Initialize (path);
				Assert.That (Directory.Exists (Path.Combine (path, ".hg")), string.Format ("Repository was not created at {0}", path));
			}
			
			using (var rootedClient = new CommandClient (path, null, null)) {
				Assert.AreEqual (path, rootedClient.Root, "Unexpected repository root");
			}
		}

		static string GetTemporaryPath ()
		{
			return Path.Combine (Path.GetTempPath (), DateTime.UtcNow.Ticks.ToString ());
		}
	}
}

