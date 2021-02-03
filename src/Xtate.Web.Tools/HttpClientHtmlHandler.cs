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
using System.Net;
using System.Net.Mime;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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

			var stream = webResponse.GetResponseStream();

			Infrastructure.NotNull(stream);

			XtateCore.Use();
			await using (stream.ConfigureAwait(false))
			{
				var encoding = new ContentType(webResponse.ContentType).CharSet is { Length: >0 } charSet ? Encoding.GetEncoding(charSet) : default;

				return await HtmlParser.TryParseHtmlAsync(stream, encoding, parameters, token).ConfigureAwait(false);
			}
		}
	}
}