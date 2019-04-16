// Copyright © FUJIWARA, Yusuke 
// This file is licensed to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.IO;

namespace Wisteria.IO.LayeredStreams
{
	/// <summary>
	///		Represents context information to create <see cref="Stream"/> effectively and efficiently.
	/// </summary>
	public readonly struct StreamFactoryContext
	{
		public long NewLength { get; }

		internal StreamFactoryContext(long newLength)
		{
			this.NewLength = newLength;
		}
	}
}
