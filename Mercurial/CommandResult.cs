using System;

namespace Mercurial
{
	public class CommandResult
	{
		public string Output { get; private set; }
		public string Error { get; private set; }
		public int Result { get; private set; }

		public CommandResult (string output, string error, int result)
		{
			Output = output;
			Error = error;
			Result = result;
		}
	}
}

