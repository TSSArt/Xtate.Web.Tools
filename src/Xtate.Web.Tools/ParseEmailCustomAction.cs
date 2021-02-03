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
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using MimeKit;
using Xtate.Service;

#if !NET461 && !NETSTANDARD2_0
using System.Buffers;

#endif

namespace Xtate.CustomAction
{
	public class ParseEmailCustomAction : CustomActionBase
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

			return Parse(content, parameters);
		}

		private static DataModelValue Parse(string content, DataModelList parameters)
		{
			MimeMessage message;
			var encoding = Encoding.ASCII;

#if NET461 || NETSTANDARD2_0
			using (var stream = new MemoryStream(encoding.GetBytes(content)))
			{
				message = MimeMessage.Load(stream);
			}
#else
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
}