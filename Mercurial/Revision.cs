using System;
using System.Xml;

namespace Mercurial
{
	public class Revision
	{
		public string RevisionId { get; private set; }
		public DateTime Date { get; private set; }
		public string Author { get; private set; }
		public string Message { get; private set; }
		
		public Revision (string revision, DateTime date, string author, string message)
		{
			RevisionId = revision;
			Date = date;
			Author = author;
			Message = message;
		}
		
		internal Revision (XmlNode node)
		{
			RevisionId = node.Attributes["revision"].Value;
			Date = DateTime.Parse (node.SelectSingleNode ("date").InnerText);
			Author = node.SelectSingleNode ("author").Attributes["email"].Value;
			Message = node.SelectSingleNode ("msg").InnerText;
		}
		
	}
}
