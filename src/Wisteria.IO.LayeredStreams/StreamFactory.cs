// Copyright © FUJIWARA, Yusuke 
// This file is licensed to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

namespace Wisteria.IO.LayeredStreams
{
	/// <summary>
	///		A delegate for <see cref="Stream"/> factory types which are used in <see cref="LayeredStream"/>.
	/// </summary>
	/// <param name="context">An ignorable context information to create a new <see cref="Stream"/> effectively and efficiently.</param>
	/// <returns><see cref="StreamInfo"/> which contains valid (readable, writeable, and seekable) <see cref="Stream"/> and optional context information.</returns>
	public delegate StreamInfo StreamFactory(in StreamFactoryContext context);
}
