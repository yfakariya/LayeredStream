// Copyright © FUJIWARA, Yusuke 
// This file is licensed to you under the MIT license.
// See the LICENSE file in the project root for more information.

#nullable enable

using NUnit.Framework;
using System;
using System.IO;

namespace Wisteria.IO.LayeredStreams
{
	[TestFixture]
	public class LayeredStreamTest
	{
		// {Write, Seek} x {Buffer, Buck} x {0, 1, threshold-1, threshold, threshold+1}
		// {Read, CopyTo, Flush} x {Buffer, Buck}

		[Test]
		public void DefaultConstructor_InitializedAsDefault()
		{
			var target = new LayeredStream();
			Assert.That(target.DebugEndoscope.BackedStream, Is.Null);
			Assert.That(target.DebugEndoscope.BackedStreamCleaner, Is.Not.Null);
			Assert.That(target.DebugEndoscope.BackedStreamContext, Is.Null);
			Assert.That(target.DebugEndoscope.BackedStreamFactory, Is.Not.Null);
			Assert.That(target.DebugEndoscope.BufferStream, Is.Not.Null.And.InstanceOf<MemoryStream>());
			Assert.That(target.DebugEndoscope.BufferStreamCleaner, Is.Not.Null);
			Assert.That(target.DebugEndoscope.BackedStreamContext, Is.Null);
			Assert.That(target.CanRead, Is.True);
			Assert.That(target.CanSeek, Is.True);
			Assert.That(target.CanWrite, Is.True);

			Assert.That(target.CanTimeout, Is.EqualTo(target.DebugEndoscope.BufferStream!.CanTimeout));
			if (target.CanTimeout)
			{
				Assert.That(target.ReadTimeout, Is.EqualTo(target.DebugEndoscope.BufferStream!.ReadTimeout));
				Assert.That(target.WriteTimeout, Is.EqualTo(target.DebugEndoscope.BufferStream!.WriteTimeout));
			}
			else
			{
				Assert.That(() => target.ReadTimeout, Throws.InvalidOperationException);
				Assert.That(() => target.WriteTimeout, Throws.InvalidOperationException);
			}

			Assert.That(target.Position, Is.EqualTo(target.DebugEndoscope.BufferStream!.Position));
			Assert.That(target.Length, Is.EqualTo(target.DebugEndoscope.BufferStream!.Length));
			Assert.That(target.Position, Is.EqualTo(0));
			Assert.That(target.Length, Is.EqualTo(0L));
		}

		private sealed class TestStream : Stream
		{
			public Stream Underlying { get; set; }

			private bool? _canRead;

			public override bool CanRead => this._canRead ?? this.Underlying.CanRead;

			public void SetCanRead(bool? value) => this._canRead = value;

			private bool? _canSeek;

			public override bool CanSeek => this._canSeek ?? this.Underlying.CanSeek;

			public void SetCanSeek(bool? value) => this._canSeek = value;

			private bool? _canWrite;

			public override bool CanWrite => this._canWrite ?? this.Underlying.CanWrite;

			public void SetCanWrite(bool? value) => this._canWrite = value;

			public override long Length => this.Underlying.Length;

			private bool? _canTimeout;

			public override bool CanTimeout => this._canTimeout ?? this.Underlying.CanTimeout;

			public void SetCanTimeout(bool? value) => this._canTimeout = value;

			public override int ReadTimeout
			{
				get => this.Underlying.ReadTimeout;
				set => this.Underlying.ReadTimeout = value;
			}

			public override int WriteTimeout
			{
				get => this.Underlying.WriteTimeout;
				set => this.Underlying.WriteTimeout = value;
			}

			public bool? Disposed { get; private set; }

			public int FlushCount { get; private set; }

			public override long Position
			{
				get => this.Underlying.Position;
				set => this.Underlying.Position = value;
			}

			public TestStream(Stream underlying)
			{
				this.Underlying = underlying;
			}

			protected override void Dispose(bool disposing)
			{
				if (disposing)
				{
					this.Underlying.Dispose();
				}

				base.Dispose(disposing);
				this.Disposed = disposing;
			}

			public override void Flush()
			{
				this.Underlying.Flush();
				this.FlushCount++;
			}

			public override int Read(byte[] buffer, int offset, int count)
				=> this.Underlying.Read(buffer, offset, count);

			public override long Seek(long offset, SeekOrigin origin)
				=> this.Underlying.Seek(offset, origin);

			public override void SetLength(long value)
				=> this.Underlying.SetLength(value);

			public override void Write(byte[] buffer, int offset, int count)
				=> this.Underlying.Write(buffer, offset, count);
		}
	}
}
