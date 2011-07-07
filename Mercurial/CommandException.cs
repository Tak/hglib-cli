using System;

namespace Mercurial
{
	public class CommandException: Exception
	{
		public CommandException (string message):
			base (message)
		{
		}
		
		public CommandException (string message, Exception innerException):
			base (message, innerException)
		{
		}
		
		public CommandException (string message, CommandResult result):
			this (string.Format ("{0}\n{1}\n{2}", message, result.Error, result.Output))
		{
		}
	}
}

