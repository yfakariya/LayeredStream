// Copyright © FUJIWARA, Yusuke 
// This file is licensed to you under the MIT license.
// See the LICENSE file in the project root for more information.

#nullable enable

using System;
using System.IO;

namespace Wisteria.IO.LayeredStreams
{
	/// <summary>
	///		Represents information for created <see cref="Stream"/>.
	/// </summary>
	public readonly struct StreamInfo
	{
		private readonly Stream _stream;

		/// <summary>
		///		Gets a created <see cref="Stream"/> object.
		/// </summary>
		/// <value>Created <see cref="Stream"/>.</value>
		public Stream Stream => this._stream;

		private readonly object? _context;

		/// <summary>
		///		Gets a context information for clean-up delegate.
		/// </summary>
		/// <value>Context information for clean-up delegate.</value>
		public object? Context => this._context;

		/// <summary>
		///		Initialize new <see cref="StreamInfo"/> object.
		/// </summary>
		/// <param name="stream">Created <see cref="Stream"/>.</param>
		/// <param name="context">Context information for clean-up delegate.</param>
		public StreamInfo(Stream stream, object? context = null)
		{
			this._stream = stream ?? throw new ArgumentNullException(nameof(stream));
			this._context = context;
		}
	}
}
