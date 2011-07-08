// 
//  Revision.cs
//  
//  Author:
//       Levi Bard <levi@unity3d.com>
//  
//  Copyright (c) 2011 Levi Bard
// 
//  This program is free software: you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as published by
//  the Free Software Foundation, either version 3 of the License, or
//  (at your option) any later version.
// 
//  This program is distributed in the hope that it will be useful,
//  but WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//  GNU Lesser General Public License for more details.
// 
//  You should have received a copy of the GNU Lesser General Public License
//  along with this program.  If not, see <http://www.gnu.org/licenses/>.

using System;
using System.Xml;

namespace Mercurial
{
	/// <summary>
	/// Represents a mercurial revision
	/// </summary>
	public class Revision
	{
		/// <summary>
		/// The revision number
		/// </summary>
		public string RevisionId { get; private set; }
		
		/// <summary>
		/// The date the revision was created
		/// </summary>
		public DateTime Date { get; private set; }
		
		/// <summary>
		/// The author of the revision
		/// </summary>
		public string Author { get; private set; }
		
		/// <summary>
		/// The commit message for the revision
		/// </summary>
		public string Message { get; private set; }
		
		internal Revision (string revision, DateTime date, string author, string message)
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
