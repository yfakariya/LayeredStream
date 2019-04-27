// Copyright © FUJIWARA, Yusuke 
// This file is licensed to you under the MIT license.
// See the LICENSE file in the project root for more information.

#nullable enable

using NUnit.Framework;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace Wisteria.IO.LayeredStreams
{
	[TestFixture]
	public class LayeredStreamTest
	{
		private const int Threshold = 3;

		private static byte[] BufferStreamContents => new byte[Threshold] { 1, 2, 3 };

		private static byte[] BackedStreamContents => new byte[Threshold] { 11, 12, 13 };

		private static int MaxReadLength => Threshold * 2;

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

		[Test]
		public void ParameterizedConstructor_AllPropertiesAreReflected()
		{
			var bufferStream = new MemoryStream();
			var bufferStreamContext = new object();

			var options =
				new LayeredStreamOptions
				{
					BackedStreamCleaner = _ => { },
					BackedStreamFactory = _ => new StreamInfo(new MemoryStream()),
					BufferStreamCleaner = _ => { },
					BufferStreamFactory = _ => new StreamInfo(bufferStream, bufferStreamContext),
					InitialCapacity = 123,
					PreferAsync = true,
					ThresholdInBytes = 234
				};
			using var target = new LayeredStream(options);

			Assert.That(target.DebugEndoscope.BackedStream, Is.Null);
			Assert.That(target.DebugEndoscope.BackedStreamCleaner, Is.SameAs(options.BackedStreamCleaner));
			Assert.That(target.DebugEndoscope.BackedStreamContext, Is.Null);
			Assert.That(target.DebugEndoscope.BackedStreamFactory, Is.SameAs(options.BackedStreamFactory));
			Assert.That(target.DebugEndoscope.BufferStream, Is.SameAs(bufferStream));
			Assert.That(target.DebugEndoscope.BufferStreamCleaner, Is.SameAs(options.BufferStreamCleaner));
			Assert.That(target.DebugEndoscope.BufferStreamContext, Is.SameAs(bufferStreamContext));
			Assert.That(target.DebugEndoscope.EffectiveStream, Is.SameAs(bufferStream));
		}

		[Test]
		[TestCase(true, false, false, false, Description = "BufferStreamFactory is null")]
		[TestCase(false, true, false, false, Description = "BufferStreamCleaner is null")]
		[TestCase(false, false, true, false, Description = "BackedStreamFactory is null")]
		[TestCase(false, false, false, true, Description = "BackedStreamCleaner is null")]
		public void ParameterizedConstructor_FactoryAndCleanerShouldBeSymmetric_ArgumentException(
			bool isBufferStreamFactoryNull,
			bool isBufferStreamCleanerNull,
			bool isBackedStreamFactoryNull,
			bool isBackedStreamCleanerNull
		) => Assert.That(
				() =>
					new LayeredStream(
						new LayeredStreamOptions
						{
							BackedStreamCleaner = isBackedStreamCleanerNull ? default(Action<StreamInfo>) : (_ => { }),
							BackedStreamFactory = isBackedStreamFactoryNull ? default(Func<StreamFactoryContext, StreamInfo>) : (_ => new StreamInfo(new MemoryStream())),
							BufferStreamCleaner = isBufferStreamCleanerNull ? default(Action<StreamInfo>) : (_ => { }),
							BufferStreamFactory = isBufferStreamFactoryNull ? default(Func<StreamFactoryContext, StreamInfo>) : (_ => new StreamInfo(new MemoryStream())),
						}
					),
				Throws.ArgumentException.And.Property("ParamName").EqualTo("options")
			);

		[Test]
		[TestCase(true, true, true, true, Description = "All null")]
		[TestCase(true, true, false, false, Description = "BufferStreamFactory and BufferStreamCleaner are null")]
		[TestCase(false, false, true, true, Description = "BackedStreamFactory and BackedStreamCleaner are null")]
		public void ParameterizedConstructor_FactoryAndCleanerShouldBeSymmetric_Default(
			bool isBufferStreamFactoryNull,
			bool isBufferStreamCleanerNull,
			bool isBackedStreamFactoryNull,
			bool isBackedStreamCleanerNull
		)
		{
			var bufferStream = new MemoryStream();
			var options =
				new LayeredStreamOptions
				{
					BackedStreamCleaner = isBackedStreamCleanerNull ? default(Action<StreamInfo>) : (_ => { }),
					BackedStreamFactory = isBackedStreamFactoryNull ? default(Func<StreamFactoryContext, StreamInfo>) : (_ => new StreamInfo(new MemoryStream())),
					BufferStreamCleaner = isBufferStreamCleanerNull ? default(Action<StreamInfo>) : (_ => { }),
					BufferStreamFactory = isBufferStreamFactoryNull ? default(Func<StreamFactoryContext, StreamInfo>) : (_ => new StreamInfo(bufferStream)),
				};

			using var target = new LayeredStream(options);

			if (isBufferStreamCleanerNull)
			{
				Assert.That(target.DebugEndoscope.BufferStreamCleaner, Is.Not.Null);
				Assert.That(target.DebugEndoscope.BufferStream, Is.TypeOf<MemoryStream>());
			}
			else
			{
				Assert.That(target.DebugEndoscope.BufferStreamCleaner, Is.SameAs(options.BufferStreamCleaner));
				Assert.That(target.DebugEndoscope.BufferStream, Is.SameAs(bufferStream));
			}

			if (isBackedStreamCleanerNull)
			{
				Assert.That(target.DebugEndoscope.BackedStreamCleaner, Is.Not.Null);
				Assert.That(target.DebugEndoscope.BackedStreamFactory, Is.Not.Null);
			}
			else
			{
				Assert.That(target.DebugEndoscope.BackedStreamCleaner, Is.SameAs(options.BackedStreamCleaner));
				Assert.That(target.DebugEndoscope.BackedStreamFactory, Is.SameAs(options.BackedStreamFactory));
			}
		}

		private static void TestDispose(bool shouldBufferStreamCleanerCalled, bool shouldBackedStreamCleanerCalled, Action<LayeredStream> actionBeforeDispose)
		{
			var isBackedStreamCleanerCalled = false;
			var isBufferStreamCleanerCalled = false;
			var target =
				new LayeredStream(
					new LayeredStreamOptions
					{
						BackedStreamCleaner =
							s =>
							{
								isBackedStreamCleanerCalled = true;
								s.Stream.Dispose();
							},
						BackedStreamFactory = _ => new StreamInfo(new MemoryStream()),
						BufferStreamCleaner =
							s =>
							{
								isBufferStreamCleanerCalled = true;
								s.Stream.Dispose();
							},
						BufferStreamFactory = _ => new StreamInfo(new MemoryStream()),
					}
				);
			actionBeforeDispose?.Invoke(target);
			target.Dispose();
			Assert.That(isBackedStreamCleanerCalled, Is.EqualTo(shouldBackedStreamCleanerCalled), "BackedStreamCleanerCalled");
			Assert.That(isBufferStreamCleanerCalled, Is.EqualTo(shouldBufferStreamCleanerCalled), "BufferStreamCleanerCalled");
		}

		[Test]
		public void Dispose_NotSwapped_BufferStreamCleanerOnlyCalled()
		{
			var isBackedStreamCleanerCalled = false;
			var isBufferStreamCleanerCalled = false;
			var target =
				new LayeredStream(
					new LayeredStreamOptions
					{
						BackedStreamCleaner =
							s =>
							{
								isBackedStreamCleanerCalled = true;
								s.Stream.Dispose();
							},
						BackedStreamFactory = _ => new StreamInfo(new MemoryStream(), new object()),
						BufferStreamCleaner =
							s =>
							{
								isBufferStreamCleanerCalled = true;
								s.Stream.Dispose();
							},
						BufferStreamFactory = _ => new StreamInfo(new MemoryStream(), new object()),
					}
				);

			Assert.That(isBackedStreamCleanerCalled, Is.False, "BackedStreamCleanerCalled");
			Assert.That(isBufferStreamCleanerCalled, Is.False, "BufferStreamCleanerCalled");

			target.Dispose();

			Assert.That(isBackedStreamCleanerCalled, Is.False, "BackedStreamCleanerCalled");
			Assert.That(isBufferStreamCleanerCalled, Is.True, "BufferStreamCleanerCalled");

			Assert.That(target.DebugEndoscope.BackedStream, Is.Null, "BackedStream");
			Assert.That(target.DebugEndoscope.BackedStreamContext, Is.Null, "BackedStreamContext");
			Assert.That(target.DebugEndoscope.BufferStream, Is.Null, "BufferStream");
			Assert.That(target.DebugEndoscope.BufferStreamContext, Is.Null, "BufferStream");
		}

		[Test]
		public void Dispose_Swapped_BufferStreamCleanerCalledOnSwapAndThenBackedStreamCleanerCalled()
		{
			var isBackedStreamCleanerCalled = false;
			var isBufferStreamCleanerCalled = false;
			var target =
				new LayeredStream(
					new LayeredStreamOptions
					{
						BackedStreamCleaner =
							s =>
							{
								isBackedStreamCleanerCalled = true;
								s.Stream.Dispose();
							},
						BackedStreamFactory = _ => new StreamInfo(new MemoryStream(), new object()),
						BufferStreamCleaner =
							s =>
							{
								isBufferStreamCleanerCalled = true;
								s.Stream.Dispose();
							},
						BufferStreamFactory = _ => new StreamInfo(new MemoryStream(), new object()),
					}
				);
			Assert.That(isBackedStreamCleanerCalled, Is.False, "BackedStreamCleanerCalled");
			Assert.That(isBufferStreamCleanerCalled, Is.False, "BufferStreamCleanerCalled");

			target.DebugEndoscope.SwapStream(1);

			Assert.That(isBackedStreamCleanerCalled, Is.False, "BackedStreamCleanerCalled");
			Assert.That(isBufferStreamCleanerCalled, Is.True, "BufferStreamCleanerCalled");

			target.Dispose();

			Assert.That(isBackedStreamCleanerCalled, Is.True, "BackedStreamCleanerCalled");
			Assert.That(isBufferStreamCleanerCalled, Is.True, "BufferStreamCleanerCalled");

			Assert.That(target.DebugEndoscope.BackedStream, Is.Null, "BackedStream");
			Assert.That(target.DebugEndoscope.BackedStreamContext, Is.Null, "BackedStreamContext");
			Assert.That(target.DebugEndoscope.BufferStream, Is.Null, "BufferStream");
			Assert.That(target.DebugEndoscope.BufferStreamContext, Is.Null, "BufferStream");
		}

		[Test]
		public void Dispose_AllCanXReturnsFalse_OthersThrowsObjectDisposedException_DisposeAndCloseHarmless()
		{
			var target = new LayeredStream();
			target.Dispose();

			target.Close();
			target.Dispose();

			Assert.That(target.CanRead, Is.False, "CanRead");
			Assert.That(target.CanSeek, Is.False, "CanSeek");
			Assert.That(target.CanTimeout, Is.False, "CanTimeout");
			Assert.That(target.CanWrite, Is.False, "CanWriteCanWrite");

			Assert.That(() => target.Length, Throws.InstanceOf<ObjectDisposedException>(), "get_Length");
			Assert.That(() => target.Position, Throws.InstanceOf<ObjectDisposedException>(), "get_Position");
			Assert.That(() => target.ReadTimeout, Throws.InstanceOf<ObjectDisposedException>(), "get_ReadTimeout");
			Assert.That(() => target.WriteTimeout, Throws.InstanceOf<ObjectDisposedException>(), "get_WriteTimeout");
			Assert.That(() => target.Position = 0, Throws.InstanceOf<ObjectDisposedException>(), "set_Position");
			Assert.That(() => target.ReadTimeout = 1, Throws.InstanceOf<ObjectDisposedException>(), "set_ReadTimeout");
			Assert.That(() => target.WriteTimeout = 1, Throws.InstanceOf<ObjectDisposedException>(), "set_WriteTimeout");

			// Followings check CanXxx at first, and then throw NotSupportedException.
			// This should be implementation detail, but we believe that it should be maitained in future for backward compatibility...
			Assert.That(() => target.BeginRead(new byte[1], 0, 1, null, null), Throws.InstanceOf<NotSupportedException>(), "BeginRead(...)");
			Assert.That(() => target.BeginWrite(new byte[1], 0, 1, null, null), Throws.InstanceOf<NotSupportedException>(), "BeginWrite(...)");
			// We cannot test EndRead/EndWrite in this point... it should be implemented correctly in Stream base type.

			Assert.That(() => target.CopyTo(Stream.Null), Throws.InstanceOf<ObjectDisposedException>(), "CopyTo(Stream)");
			Assert.That(() => target.CopyTo(Stream.Null, 1), Throws.InstanceOf<ObjectDisposedException>(), "CopyTo(Stream, Int32)");
			Assert.That(async () => await target.CopyToAsync(Stream.Null), Throws.InstanceOf<ObjectDisposedException>(), "CopyToAsync(Stream)");
			Assert.That(async () => await target.CopyToAsync(Stream.Null, 1), Throws.InstanceOf<ObjectDisposedException>(), "CopyToAsync(Stream, Int32)");
			Assert.That(async () => await target.CopyToAsync(Stream.Null, CancellationToken.None), Throws.InstanceOf<ObjectDisposedException>(), "CopyToAsync(Stream, CancellationToken)");
			Assert.That(async () => await target.CopyToAsync(Stream.Null, 1, CancellationToken.None), Throws.InstanceOf<ObjectDisposedException>(), "CopyToAsync(Stream, int, CancellationToken)");

			Assert.That(() => target.Flush(), Throws.InstanceOf<ObjectDisposedException>(), "Flush()");
			Assert.That(async () => await target.FlushAsync(), Throws.InstanceOf<ObjectDisposedException>(), "FlushAsync()");
			Assert.That(async () => await target.FlushAsync(CancellationToken.None), Throws.InstanceOf<ObjectDisposedException>(), "FlushAsync(CancellationToken)");

			Assert.That(() => target.Read(new byte[1], 0, 1), Throws.InstanceOf<ObjectDisposedException>(), "Read(Byte[], Int32, Int32)");
			Assert.That(async () => await target.ReadAsync(new byte[1], 0, 1), Throws.InstanceOf<ObjectDisposedException>(), "ReadAsync(Byte[], Int32, Int32)");
			Assert.That(async () => await target.ReadAsync(new byte[1], 0, 1, CancellationToken.None), Throws.InstanceOf<ObjectDisposedException>(), "ReadAsync(Byte[], Int32, CancellationToken)");
			Assert.That(() => target.ReadByte(), Throws.InstanceOf<ObjectDisposedException>(), "ReadByte()");

			Assert.That(() => target.Write(new byte[1], 0, 1), Throws.InstanceOf<ObjectDisposedException>(), "Write(Byte[], Int32, Int32)");
			Assert.That(async () => await target.WriteAsync(new byte[1], 0, 1), Throws.InstanceOf<ObjectDisposedException>(), "WriteAsync(Byte[], Int32, Int32)");
			Assert.That(async () => await target.WriteAsync(new byte[1], 0, 1, CancellationToken.None), Throws.InstanceOf<ObjectDisposedException>(), "WriteAsync(Byte[], Int32, CancellationToken)");
			Assert.That(() => target.WriteByte(0), Throws.InstanceOf<ObjectDisposedException>(), "WriteByte(Byte)");

			Assert.That(async () => await target.ReadAsync(new Memory<byte>(), CancellationToken.None), Throws.InstanceOf<ObjectDisposedException>(), "ReadAsync(Memory<Byte>, CancellationToken)");
			Assert.That(async () => await target.WriteAsync(new ReadOnlyMemory<byte>(), CancellationToken.None), Throws.InstanceOf<ObjectDisposedException>(), "WriteAsync(ReadOnlyMemory<Byte>, CancellationToken)");

			Assert.That(() => target.Seek(0, SeekOrigin.Current), Throws.InstanceOf<ObjectDisposedException>(), "Seek(Int64, SeekOrigin)");
			Assert.That(() => target.SetLength(0), Throws.InstanceOf<ObjectDisposedException>(), "SetLength(Int64)");
		}

		private static void TestWriteCore(Action<LayeredStream> action)
		{
			using var target =
				new LayeredStream(
					new LayeredStreamOptions
					{
						ThresholdInBytes = Threshold
					}
				);

			action(target);
		}

		[Test]
		[TestCase(0, Description = "Zero")]
		[TestCase(1, Description = "Single")]
		[TestCase(2, Description = "Multiple")]
		[TestCase(3, Description = "Just reached")]
		public void Write_ToOnlyBuffer(int count)
			=> TestWriteCore(
				target =>
				{
					var buffer = Enumerable.Range(21, count).Select(x => (byte)x).ToArray();
					target.Write(buffer, 0, buffer.Length);
					Assert.That(target.Position, Is.EqualTo(count));
					Assert.That(target.Length, Is.EqualTo(count));
					Assert.That(target.DebugEndoscope.EffectiveStream, Is.SameAs(target.DebugEndoscope.BufferStream));
					var readBuffer = new byte[buffer.Length];
					target.DebugEndoscope.BufferStream!.Position = 0;
					target.DebugEndoscope.BufferStream!.Read(readBuffer, 0, readBuffer.Length);
					Assert.That(readBuffer, Is.EqualTo(buffer));
				}
			);

		[Test]
		[TestCase(4, Description = "Exceeds 1")]
		[TestCase(5, Description = "Exceeds 2")]
		public void Write_ToBacked(int count)
			=> TestWriteCore(
				target =>
				{
					var buffer = Enumerable.Range(21, count).Select(x => (byte)x).ToArray();
					target.Write(buffer, 0, buffer.Length);
					Assert.That(target.Position, Is.EqualTo(count));
					Assert.That(target.Length, Is.EqualTo(count));
					Assert.That(target.DebugEndoscope.EffectiveStream, Is.SameAs(target.DebugEndoscope.BackedStream));
					var readBuffer = new byte[buffer.Length];
					target.DebugEndoscope.BackedStream!.Position = 0;
					target.DebugEndoscope.BackedStream!.Read(readBuffer, 0, readBuffer.Length);
					Assert.That(readBuffer, Is.EqualTo(buffer));
				}
			);


		private static void TestReadCore(Action<LayeredStream> action)
		{
			var bufferStream = new MemoryStream(BufferStreamContents);
			var backedStream = new MemoryStream(BackedStreamContents);
			using var target =
				new LayeredStream(
					new LayeredStreamOptions
					{
						BackedStreamFactory = _ => new StreamInfo(backedStream),
						BackedStreamCleaner = x => x.Stream.Dispose(),
						BufferStreamFactory = _ => new StreamInfo(bufferStream),
						BufferStreamCleaner = x => x.Stream.Dispose(),
						ThresholdInBytes = Threshold
					}
				);

			action(target);
		}

		[Test]
		[TestCase(0, Description = "Zero")]
		[TestCase(1, Description = "Single")]
		[TestCase(2, Description = "Multiple")]
		[TestCase(3, Description = "Just reached")]
		public void Read_FromBuffer(int count)
			=> TestReadCore(
				target =>
				{
					var buffer = new byte[count + 2];
					var actualReadCount = target.Read(buffer, 1, count);
					Assert.That(actualReadCount, Is.EqualTo(count));
					Assert.That(buffer.First(), Is.EqualTo(0));
					Assert.That(buffer.Skip(count + 1).First(), Is.EqualTo(0));
					Assert.That(buffer.Skip(1).Take(count), Is.EqualTo(BufferStreamContents.Take(count)));

					Assert.That(target.DebugEndoscope.EffectiveStream, Is.SameAs(target.DebugEndoscope.BufferStream));
				}
			);

		[Test]
		public void Read_ExceedsBuffer_OnlyFromBuffer()
			=> TestReadCore(
				target =>
				{
					var buffer = new byte[BufferStreamContents.Length + 2];
					var actualReadCount = target.Read(buffer, 1, BufferStreamContents.Length + 1);
					Assert.That(actualReadCount, Is.EqualTo(BufferStreamContents.Length));
					Assert.That(buffer.First(), Is.EqualTo(0));
					Assert.That(buffer.Skip(BufferStreamContents.Length + 1).First(), Is.EqualTo(0));
					Assert.That(buffer.Skip(1).Take(BufferStreamContents.Length), Is.EqualTo(BufferStreamContents));

					// Should not be swap on Read
					Assert.That(target.DebugEndoscope.EffectiveStream, Is.SameAs(target.DebugEndoscope.BufferStream));
				}
			);

		[Test]
		[TestCase(0, Description = "Zero")]
		[TestCase(1, Description = "Single")]
		[TestCase(2, Description = "Multiple")]
		[TestCase(3, Description = "Just reached")]
		public void Read_FromBacked(int count)
			=> TestReadCore(
				target =>
				{
					target.DebugEndoscope.SwapStream(Threshold);
					Assert.That(target.DebugEndoscope.EffectiveStream, Is.SameAs(target.DebugEndoscope.BackedStream));
					target.DebugEndoscope.EffectiveStream.Position = 0;

					var buffer = new byte[count + 2];
					var actualReadCount = target.Read(buffer, 1, count);
					Assert.That(actualReadCount, Is.EqualTo(Math.Min(count, MaxReadLength)));
					Assert.That(buffer.First(), Is.EqualTo(0));
					Assert.That(buffer.Skip(Math.Min(count, MaxReadLength) + 1).First(), Is.EqualTo(0));
					Assert.That(
						buffer.Skip(1).Take(Math.Min(count, MaxReadLength)),
						Is.EqualTo(BufferStreamContents.Concat(BackedStreamContents).Take(Math.Min(count, MaxReadLength)))
					);

					Assert.That(target.DebugEndoscope.BufferStream, Is.Null);
					Assert.That(target.DebugEndoscope.BackedStream, Is.Not.Null);
					Assert.That(target.DebugEndoscope.EffectiveStream, Is.SameAs(target.DebugEndoscope.BackedStream));
				}
			);

#warning TODO: Default -> Buffer/File including temp file handling
#warning TODO: SetLength, CanXxx check, Seek, Flush
#warning TODO: stream verification


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
