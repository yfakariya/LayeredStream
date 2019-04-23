// Copyright © FUJIWARA, Yusuke 
// This file is licensed to you under the MIT license.
// See the LICENSE file in the project root for more information.

#nullable enable

using System;

namespace Wisteria.IO.LayeredStreams
{
	/// <summary>
	///		Represents option settings for <see cref="LayeredStream"/>.
	/// </summary>
	public struct LayeredStreamOptions
	{
		private int _thresholdInBytes;
		public int ThresholdInBytes
		{
			get => this._thresholdInBytes;
			set
			{
				if (value <= 0)
				{
					throw new ArgumentOutOfRangeException(nameof(value), "ThresholdInBytes must be greater than 0.");
				}

				this._thresholdInBytes = value;
			}
		}

		public Func<StreamFactoryContext, StreamInfo>? BufferStreamFactory { get; set; }
		public Action<StreamInfo>? BufferStreamCleaner { get; set; }
		public Func<StreamFactoryContext, StreamInfo>? BackedStreamFactory { get; set; }
		public Action<StreamInfo>? BackedStreamCleaner { get; set; }
		public bool PreferAsync { get; set; }
		private int _initialCapacity;
		public int InitialCapacity
		{
			get => this._initialCapacity;
			set
			{
				if (value < 0)
				{
					throw new ArgumentOutOfRangeException(nameof(value), "InitialCapacity must not be negative.");
				}

				this._initialCapacity = value;
			}
		}

		internal void Verify()
		{
			VerifyFactoryAndCleanerAreNotSymmetric(
				this.BufferStreamFactory,
				this.BufferStreamCleaner,
				nameof(this.BufferStreamFactory),
				nameof(this.BufferStreamCleaner)
			);
			VerifyFactoryAndCleanerAreNotSymmetric(
				this.BackedStreamFactory,
				this.BackedStreamCleaner,
				nameof(this.BackedStreamFactory),
				nameof(this.BackedStreamCleaner)
			);
		}

		private static void VerifyFactoryAndCleanerAreNotSymmetric(StreamFactory? factory, Action<StreamInfo>? cleaner, string factoryName, string cleanerName)
		{
			if (factory == null)
			{
				if (cleaner != null)
				{
					ThrowFactoryAndCleanerAreNotSymmetric(factoryName, cleanerName);
				}
			}
			else if (cleaner == null)
			{
				ThrowFactoryAndCleanerAreNotSymmetric(factoryName, cleanerName);
			}
		}

		private static void ThrowFactoryAndCleanerAreNotSymmetric(string factoryName, string cleanerName)
			=> throw new InvalidOperationException($"Both of {factoryName} and {cleanerName} are null or non-null.");
	}
}
