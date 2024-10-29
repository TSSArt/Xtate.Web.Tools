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

using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using MimeKit;
using Xtate.Service;
#if NETCOREAPP2_1_OR_GREATER || NETSTANDARD2_1
using System.Buffers;

#endif

namespace Xtate.CustomAction;

public class ParseEmailActionProvider() : ActionProvider<ParseEmailAction>(ns: "http://xtate.net/scxml/webtools", name: "parseEmail");

public class ParseEmailAction(XmlReader xmlReader) : SyncAction
{
	private readonly ObjectValue _capture = new(xmlReader.GetAttribute("captureExpr"), xmlReader.GetAttribute("capture"));

	private readonly StringValue _content = new(xmlReader.GetAttribute("contentExpr"), xmlReader.GetAttribute("content"));

	private readonly Location _result = new(xmlReader.GetAttribute("result"));

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

		return Parse(_content.Value, parameters);
	}

	private static DataModelValue Parse(string content, DataModelList parameters)
	{
		MimeMessage message;
		var encoding = Encoding.ASCII;

#if NETCOREAPP2_1_OR_GREATER || NETSTANDARD2_1
		var bytes = ArrayPool<byte>.Shared.Rent(encoding.GetMaxByteCount(content.Length));

		try
		{
			var length = encoding.GetBytes(content, bytes);
			using var stream = new MemoryStream(bytes, index: 0, length);
			message = MimeMessage.Load(stream);
		}
		finally
		{
			ArrayPool<byte>.Shared.Return(bytes);
		}
#else
		using (var stream = new MemoryStream(encoding.GetBytes(content)))
		{
			message = MimeMessage.Load(stream);
		}
#endif

		if (message.HtmlBody is { } html)
		{
			return HtmlParser.TryParseHtml(html, parameters);
		}

		if (message.TextBody is not { } text)
		{
			return default;
		}

		if (parameters["regex"].AsStringOrDefault() is not { } pattern)
		{
			return text;
		}

		var regex = new Regex(pattern);
		var match = regex.Match(text);

		if (!match.Success)
		{
			return default;
		}

		if (match.Groups.Count == 1)
		{
			return match.Groups[0].Value;
		}

		var groupNames = regex.GetGroupNames();

		var list = new DataModelList();

		foreach (var name in groupNames)
		{
			list.Add(name, match.Groups[name].Value);
		}

		return list;
	}
}