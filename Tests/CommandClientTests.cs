using System;
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
	}
}

