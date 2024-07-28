// Copyright © 2019-2024 Sergii Artemenko
// 
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

using System.Threading.Tasks;
using System.Xml;
using Xtate.Service;

namespace Xtate.CustomAction;

public class ParseHtmlCustomActionProvider() : CustomActionProvider<ParseEmailCustomAction>(ns: "http://xtate.net/scxml/webtools", name: "parseHtml");

public class ParseHtmlCustomAction(XmlReader xmlReader) : CustomActionBase
{
	private readonly Value       _capture = new(xmlReader.GetAttribute("captureExpr"), xmlReader.GetAttribute("capture"));
	private readonly StringValue _content = new(xmlReader.GetAttribute("contentExpr"), xmlReader.GetAttribute("content"));
	private readonly Location    _result  = new(xmlReader.GetAttribute("result"));

	public override IEnumerable<Value> GetValues()
	{
		yield return _content;
		yield return _capture;
	}

	public override IEnumerable<Location> GetLocations() { yield return _result; }

	public override async ValueTask Execute()
	{
		var content = await _content.GetValue().ConfigureAwait(false);

		if (content is not null)
		{
			var capture = DataModelValue.FromObject(await _capture.GetValue().ConfigureAwait(false)).AsListOrEmpty();

			var parameters = new DataModelList { { @"capture", capture } };

			var result = HtmlParser.TryParseHtml(content, parameters);

			await _result.SetValue(result).ConfigureAwait(false);
		}
	}
}