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

public class ParseHtmlActionProvider() : ActionProvider<ParseHtmlAction>(ns: "http://xtate.net/scxml/webtools", name: "parseHtml");

public class ParseHtmlAction(XmlReader xmlReader) : SyncAction
{
	private readonly ObjectValue _capture = new(xmlReader.GetAttribute("captureExpr"), xmlReader.GetAttribute("capture"));
	private readonly StringValue _content = new(xmlReader.GetAttribute("contentExpr"), xmlReader.GetAttribute("content"));
	private readonly Location    _result  = new(xmlReader.GetAttribute("result"));

	protected override IEnumerable<Value> GetValues()
	{
		yield return _content;
		yield return _capture;
	}

	protected override IEnumerable<Location> GetLocations() { yield return _result; }

	protected override DataModelValue Evaluate()
	{
		var parameters = new DataModelList
						 {
							 { @"capture", DataModelValue.FromObject(_capture.Value).AsListOrEmpty() }
						 };
		
		return HtmlParser.TryParseHtml(_content.Value, parameters);
	}
}