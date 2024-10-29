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
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Xtate.Core;

namespace Xtate.Service;

[PublicAPI]
public static class HtmlParser
{
	public static async ValueTask<DataModelValue> TryParseHtmlAsync(Stream stream,
																	Encoding? encoding,
																	DataModelList parameters,
																	CancellationToken token)
	{
		if (stream is null) throw new ArgumentNullException(nameof(stream));
		if (parameters is null) throw new ArgumentNullException(nameof(parameters));

		string html;

		using (var streamReader = new StreamReader(stream.InjectCancellationToken(token), encoding ?? Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
		{
			html = await streamReader.ReadToEndAsync().ConfigureAwait(false);
		}

		return TryParseHtml(html, parameters);
	}

	public static DataModelValue TryParseHtml(Stream stream, Encoding? encoding, DataModelList parameters)
	{
		if (stream is null) throw new ArgumentNullException(nameof(stream));
		if (parameters is null) throw new ArgumentNullException(nameof(parameters));

		string html;

		using (var streamReader = new StreamReader(stream, encoding ?? Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
		{
			html = streamReader.ReadToEnd();
		}

		return TryParseHtml(html, parameters);
	}

	public static DataModelValue TryParseHtml(string html, DataModelList parameters)
	{
		if (html is null) throw new ArgumentNullException(nameof(html));
		if (parameters is null) throw new ArgumentNullException(nameof(parameters));

		var capturesList = parameters["capture"].AsListOrEmpty();

		if (capturesList.Count == 0)
		{
			return default;
		}

		var captures = from pair in capturesList.KeyValuePairs
					   let capture = pair.Value.AsListOrEmpty()
					   select new Capture
							  {
								  Name = pair.Key,
								  XPaths = GetArray(capture["xpath"]),
								  Attributes = GetArray(capture["attr"]),
								  Regex = capture["regex"].AsStringOrDefault()
							  };

		var htmlDocument = new HtmlDocument();

		htmlDocument.LoadHtml(html);

		return CaptureData(htmlDocument, captures);
	}

	private static string[] GetArray(in DataModelValue value) =>
		value.Type == DataModelValueType.List
			? value.AsList().Select(item => item.AsString()).Where(str => !string.IsNullOrEmpty(str)).ToArray()
			: value.AsStringOrDefault() is { Length: > 0 } stringValue
				? new[] { stringValue }
				: Array.Empty<string>();

	private static DataModelValue CaptureData(HtmlDocument htmlDocument, IEnumerable<Capture> captures)
	{
		var list = new DataModelList();

		foreach (var capture in captures)
		{
			var result = CaptureEntry(htmlDocument, capture);

			if (!result.IsUndefined())
			{
				list.Add(capture.Name, result);
			}
		}

		return list;
	}

	private static DataModelValue CaptureEntry(HtmlDocument htmlDocument, in Capture capture)
	{
		if (capture.XPaths.Length == 0)
		{
			return CaptureInNode(htmlDocument.DocumentNode, capture);
		}

		var array = new DataModelList();

		foreach (var xpath in capture.XPaths)
		{
			var nodes = htmlDocument.DocumentNode.SelectNodes(xpath);

			if (nodes is null)
			{
				continue;
			}

			foreach (var node in nodes)
			{
				var result = CaptureInNode(node, capture);

				if (!result.IsUndefined())
				{
					array.Add(result);
				}
			}
		}

		return array;
	}

	private static DataModelValue CaptureInNode(HtmlNode node, in Capture capture)
	{
		if (capture.Attributes.Length == 0)
		{
			return CaptureInText(node.InnerHtml, capture);
		}

		var list = new DataModelList();

		foreach (var attr in capture.Attributes)
		{
			var value = attr.StartsWith(@"::") ? GetSpecialAttributeValue(node, attr) : node.GetAttributeValue(attr, def: null);

			if (value is null)
			{
				return default;
			}

			list.Add(attr, CaptureInText(value, capture));
		}

		return list;
	}

	private static string? GetSpecialAttributeValue(HtmlNode node, string attr) =>
		attr switch
		{
			"::value" => GetHtmlValue(node),
			_         => null
		};

	private static string? GetHtmlValue(HtmlNode node) =>
		node.Name switch
		{
			"input"    => GetInputValue(node),
			"textarea" => GetInputValue(node),
			"select"   => GetSelectValue(node),
			_          => null
		};

	private static string? GetSelectValue(HtmlNode node)
	{
		var selected = node.ChildNodes.FirstOrDefault(n => n.Name == @"option" && n.Attributes.Contains(@"selected"))
					   ?? node.ChildNodes.FirstOrDefault(n => n.Name == @"option");

		return selected is not null ? GetValue(selected, check: false) : null;
	}

	private static string? GetInputValue(HtmlNode node) =>
		node.GetAttributeValue(name: @"type", def: null) switch
		{
			"radio"    => GetValue(node, check: true),
			"checkbox" => GetValue(node, check: true),
			_          => GetValue(node, check: false)
		};

	private static string? GetValue(HtmlNode node, bool check)
	{
		if (check && !node.Attributes.Contains(@"checked"))
		{
			return null;
		}

		return node.GetAttributeValue(name: @"value", def: null) ?? node.InnerText;
	}

	private static DataModelValue CaptureInText(string text, in Capture capture)
	{
		if (capture.Regex is null)
		{
			return text;
		}

		var regex = new Regex(capture.Regex);
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

	private readonly struct Capture
	{
		public string Name { get; init; }

		public string[] XPaths { get; init; }

		public string[] Attributes { get; init; }

		public string? Regex { get; init; }
	}
}