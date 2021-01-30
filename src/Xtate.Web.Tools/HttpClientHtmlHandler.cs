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
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mime;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Xtate.Core;

namespace Xtate.Service
{
	[PublicAPI]
	public class HttpClientHtmlHandler : HttpClientMimeTypeHandler
	{
		private const string MediaTypeTextHtml = "text/html";

		private HttpClientHtmlHandler() { }

		public static HttpClientMimeTypeHandler Instance { get; } = new HttpClientHtmlHandler();

		public override void PrepareRequest(WebRequest webRequest, string? contentType, DataModelList parameters, DataModelValue value) => AppendAcceptHeader(webRequest, MediaTypeTextHtml);

		public override async ValueTask<DataModelValue?> TryParseResponseAsync(WebResponse webResponse, DataModelList parameters, CancellationToken token)
		{
			if (webResponse is null) throw new ArgumentNullException(nameof(webResponse));
			if (parameters is null) throw new ArgumentNullException(nameof(parameters));

			if (!ContentTypeEquals(webResponse.ContentType, MediaTypeTextHtml))
			{
				return default;
			}

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

			var stream = webResponse.GetResponseStream();

			Infrastructure.NotNull(stream);

			await using (stream.ConfigureAwait(false))
			{
				var contentType = new ContentType(webResponse.ContentType);

				return await FromHtmlContent(stream, contentType.CharSet, captures.ToArray(), token).ConfigureAwait(false);
			}
		}

		private static string[]? GetArray(DataModelValue val)
		{
			if (val.Type == DataModelValueType.List)
			{
				return val.AsList().Select(p => p.AsString()).Where(s => !string.IsNullOrEmpty(s)).ToArray();
			}

			var str = val.AsStringOrDefault();

			return !string.IsNullOrEmpty(str) ? new[] { str } : null;
		}

		private static async ValueTask<DataModelValue> FromHtmlContent(Stream stream, string? contentEncoding, Capture[] captures, CancellationToken token)
		{
			var encoding = contentEncoding is not null ? Encoding.GetEncoding(contentEncoding) : Encoding.UTF8;

			var htmlDocument = new HtmlDocument();

			string html;
			using (var streamReader = new StreamReader(stream.InjectCancellationToken(token), encoding))
			{
				html = await streamReader.ReadToEndAsync().ConfigureAwait(false);
			}

			htmlDocument.LoadHtml(html);

			return CaptureData(htmlDocument, captures);
		}

		private static DataModelValue CaptureData(HtmlDocument htmlDocument, Capture[] captures)
		{
			var list = new DataModelList();

			foreach (var capture in captures)
			{
				var result = CaptureEntry(htmlDocument, capture.XPaths, capture.Attributes, capture.Regex);

				if (!result.IsUndefined())
				{
					list.Add(capture.Name, result);
				}
			}

			return list;
		}

		private static DataModelValue CaptureEntry(HtmlDocument htmlDocument, string[]? xpaths, string[]? attrs, string? pattern)
		{
			if (xpaths is null)
			{
				return CaptureInNode(htmlDocument.DocumentNode, attrs, pattern);
			}

			var array = new DataModelList();

			foreach (var xpath in xpaths)
			{
				var nodes = htmlDocument.DocumentNode.SelectNodes(xpath);

				if (nodes is null)
				{
					continue;
				}

				foreach (var node in nodes)
				{
					var result = CaptureInNode(node, attrs, pattern);

					if (!result.IsUndefined())
					{
						array.Add(result);
					}
				}
			}

			return array;
		}

		private static DataModelValue CaptureInNode(HtmlNode node, string[]? attrs, string? pattern)
		{
			if (attrs is null)
			{
				return CaptureInText(node.InnerHtml, pattern);
			}

			var list = new DataModelList();

			foreach (var attr in attrs)
			{
				var value = attr.StartsWith(value: @"::", StringComparison.Ordinal) ? GetSpecialAttributeValue(node, attr) : node.GetAttributeValue(attr, def: null);

				if (value is null)
				{
					return default;
				}

				list.Add(attr, CaptureInText(value, pattern));
			}

			return list;
		}

		private static string? GetSpecialAttributeValue(HtmlNode node, string attr) =>
				attr switch
				{
						"::value" => GetHtmlValue(node),
						_ => null
				};

		private static string? GetHtmlValue(HtmlNode node) =>
				node.Name switch
				{
						"input" => GetInputValue(node),
						"textarea" => GetInputValue(node),
						"select" => GetSelectValue(node),
						_ => null
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
						"radio" => GetValue(node, check: true),
						"checkbox" => GetValue(node, check: true),
						_ => GetValue(node, check: false)
				};

		private static string? GetValue(HtmlNode node, bool check)
		{
			if (check && !node.Attributes.Contains(@"checked"))
			{
				return null;
			}

			return node.GetAttributeValue(name: @"value", def: null) ?? node.InnerText;
		}

		private static DataModelValue CaptureInText(string text, string? pattern)
		{
			if (pattern is null)
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

		private struct Capture
		{
			public string    Name       { get; set; }
			public string[]? XPaths     { get; set; }
			public string[]? Attributes { get; set; }
			public string?   Regex      { get; set; }
		}
	}
}