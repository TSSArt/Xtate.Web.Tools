#region Copyright © 2019-2021 Sergii Artemenko

// This file is part of the Xtate project. <https://xtate.net/>
// 
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU Affero General Public License as published
// by the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
// 
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU Affero General Public License for more details.
// 
// You should have received a copy of the GNU Affero General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

#endregion

using System;
using System.Collections.Generic;
using System.Xml;
using Xtate.Service;

namespace Xtate.CustomAction
{
	public class ParseHtmlCustomAction : CustomActionBase
	{
		private const string Content = "content";
		private const string Capture = "capture";
		private const string Result  = "result";

		protected override void Initialize(XmlReader xmlReader)
		{
			if (xmlReader is null) throw new ArgumentNullException(nameof(xmlReader));

			RegisterArgument(Content, ExpectedValueType.Any, xmlReader.GetAttribute(Content));
			RegisterArgument(Capture, ExpectedValueType.Any, xmlReader.GetAttribute(Capture));
			RegisterResultLocation(xmlReader.GetAttribute(Result));
		}

		protected override DataModelValue Evaluate(IReadOnlyDictionary<string, DataModelValue> args)
		{
			if (args is null) throw new ArgumentNullException(nameof(args));

			var content = args[Content].AsStringOrDefault();
			var capture = args[Capture].AsListOrEmpty();

			if (content is null)
			{
				return DataModelValue.Null;
			}

			var parameters = new DataModelList { { @"capture", capture } };

			return HtmlParser.TryParseHtml(content, parameters);
		}
	}
}