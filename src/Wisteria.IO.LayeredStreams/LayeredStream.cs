// Copyright © FUJIWARA, Yusuke 
// This file is licensed to you under the MIT license.
// See the LICENSE file in the project root for more information.

#nullable enable

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

#if !NETCOREAPP2_1
using ValueTask = System.Threading.Tasks.Task;
#endif // !NETCOREAPP2_1

namespace Wisteria.IO.LayeredStreams
{
	public sealed class LayeredStream : Stream
	{
		private readonly int _thresholdInBytes;
		private readonly Action<StreamInfo> _bufferStreamCleaner;
		private readonly StreamFactory _backedStreamFactory;
		private readonly Action<StreamInfo> _backedStreamCleaner;
		private readonly bool _preferAsync;
		private Stream? _bufferStream;
		private object? _bufferStreamContext;
		private Stream? _backedStream;
		private object? _backedStreamContext;

		private Stream EffectiveStream => this._backedStream ?? this._bufferStream!;

		public override bool CanRead => true;

		public override bool CanSeek => true;

		public override bool CanWrite => true;

		public override long Length => this.EffectiveStream.Length;

		public override long Position
		{
			get => this.EffectiveStream.Position;
			set => this.EffectiveStream.Position = value;
		}

		public LayeredStream() : this(default) { }

		public LayeredStream(in LayeredStreamOptions options)
		{
			options.Verify();

			this._backedStreamFactory = options.BackedStreamFactory ?? this.CreateDefaultBackedStream;
			this._backedStreamCleaner = options.BackedStreamCleaner ?? CleanUpDefaultBackedStream;
			var bufferStreamInfo = (options.BufferStreamFactory?? CreateDefaultBufferStream)(new StreamFactoryContext(options.InitialCapacity));
			ValidateStreamCapability(bufferStreamInfo.Stream, "buffer stream");
			this._bufferStreamContext = bufferStreamInfo.Context;
			this._bufferStream = bufferStreamInfo.Stream;
			this._bufferStreamCleaner = options.BufferStreamCleaner ?? CleanUpDefaultBufferStream;
			this._thresholdInBytes = options.ThresholdInBytes;
			this._preferAsync = options.PreferAsync;
		}

		private StreamInfo CreateDefaultBackedStream(in StreamFactoryContext context)
		{
			var file = Path.GetTempFileName();
			var stream = new FileStream(file, FileMode.Create, FileAccess.ReadWrite, FileShare.Read, 64 * 1024, this._preferAsync ? FileOptions.Asynchronous : FileOptions.None);
			return new StreamInfo(stream, file);
		}

		private static void CleanUpDefaultBackedStream(StreamInfo info)
		{
			Debug.Assert(info.Context is string, "info.Context is string");
			var path = info.Context as string;
			info.Stream.Dispose();
			File.Delete(path);
		}

		private static StreamInfo CreateDefaultBufferStream(in StreamFactoryContext context)
			=> new StreamInfo(new MemoryStream(checked((int)context.NewLength)));

		private static void CleanUpDefaultBufferStream(StreamInfo info)
			=> info.Stream.Dispose();


		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				if (this._backedStream != null)
				{
					this._backedStreamCleaner(new StreamInfo(this._backedStream, this._backedStreamContext));
					this._backedStream = null;
					this._backedStreamContext = null;
				}

				if (this._bufferStream != null)
				{
					this._bufferStreamCleaner(new StreamInfo(this._bufferStream, this._bufferStreamContext));
					this._bufferStream = null;
					this._bufferStreamContext = null;
				}
			}

			base.Dispose(disposing);
		}

		[Conditional("DEBUG")]
		private void CheckInvariant()
			=> Debug.Assert(
				(this._bufferStream == null && this._backedStream != null) || (this._bufferStream == null && this._backedStream != null),
				"(this._bufferStream == null && this._backedStream != null) || (this._bufferStream == null && this._backedStream != null)"
			);

		private void SwapStream(long newLength)
		{
			this.PrepareBackStream(newLength);
			this._bufferStream!.CopyTo(this._backedStream!);
			this.CleanUpBufferStream();
			this.CheckInvariant();
		}

		private async ValueTask SwapStreamAsync(long newLength, CancellationToken cancellationToken)
		{
			this.PrepareBackStream(newLength);
			await this._bufferStream!.CopyToAsync(this._backedStream!, 81920, cancellationToken).ConfigureAwait(false);
			this.CleanUpBufferStream();
			this.CheckInvariant();
		}

		private void PrepareBackStream(long newLength)
		{
			var context = new StreamFactoryContext(newLength);
			StreamInfo newStreamInfo = this._backedStreamFactory(in context);
			ValidateStreamCapability(newStreamInfo.Stream, "backed stream");
			this._backedStreamContext = newStreamInfo.Context;
			this._backedStream = newStreamInfo.Stream;
		}

		private void CleanUpBufferStream()
		{
			this._bufferStreamCleaner(new StreamInfo(this._bufferStream!, this._bufferStreamContext));
			this._bufferStreamContext = null;
			this._bufferStream = null;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private SwapStatus QuerySwapNeeded(int requiredCount)
		{
			if (this._bufferStream == null)
			{
				return SwapStatus.AlreadySwapped;
			}

			var newLength = this._bufferStream.Length + requiredCount;
			if (newLength > this._thresholdInBytes)
			{
				return SwapStatus.Required;
			}

			return SwapStatus.NotRequired;
		}

		private static void ValidateStreamCapability(Stream stream, string label)
		{
			if (!stream.CanRead)
			{
				throw new InvalidOperationException($"'{label}' stream must be able to read.");
			}

			if (!stream.CanWrite)
			{
				throw new InvalidOperationException($"'{label}' stream must be able to write.");
			}

			if (!stream.CanSeek)
			{
				throw new InvalidOperationException($"'{label}' stream must be able to seek.");
			}
		}

		public override void Flush()
			=> this.EffectiveStream.Flush();

		public override Task FlushAsync(CancellationToken cancellationToken)
			=> this.EffectiveStream.FlushAsync(cancellationToken);

		public override int Read(byte[] buffer, int offset, int count)
			=> this.EffectiveStream.Read(buffer, offset, count);

		public override int ReadByte()
			=> this.EffectiveStream.ReadByte();

		public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
			=> this.EffectiveStream.ReadAsync(buffer, offset, count, cancellationToken);

		public override void Write(byte[] buffer, int offset, int count)
		{
			this.CheckInvariant();

			switch (this.QuerySwapNeeded(count))
			{
				case SwapStatus.Required:
				{
					this.SwapStream(count);
					goto case SwapStatus.AlreadySwapped;
				}
				case SwapStatus.AlreadySwapped:
				{
					this._bufferStream!.Write(buffer, offset, count);
					return;
				}
			}

			this._backedStream!.Write(buffer, offset, count);
		}

		public override void WriteByte(byte value)
		{
			this.CheckInvariant();

			switch (this.QuerySwapNeeded(1))
			{
				case SwapStatus.Required:
				{
					this.SwapStream(1);
					goto case SwapStatus.AlreadySwapped;
				}
				case SwapStatus.AlreadySwapped:
				{
					this._bufferStream!.WriteByte(value);
					return;
				}
			}

			this._backedStream!.WriteByte(value);
		}

		public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
		{
			this.CheckInvariant();

			switch (this.QuerySwapNeeded(count))
			{
				case SwapStatus.Required:
				{
					await this.SwapStreamAsync(count, cancellationToken).ConfigureAwait(false);
					goto case SwapStatus.AlreadySwapped;
				}
				case SwapStatus.AlreadySwapped:
				{
					await this._bufferStream!.WriteAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
					return;
				}
			}

			await this._backedStream!.WriteAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
		}

		public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
			=> this.EffectiveStream.CopyToAsync(destination, bufferSize, cancellationToken);

		public override long Seek(long offset, SeekOrigin origin)
		{
			var newPosition =
				origin switch
			{
				SeekOrigin.Begin => offset,
				SeekOrigin.Current => this.EffectiveStream.Position + offset,
				SeekOrigin.End => this.EffectiveStream.Length + offset,
				_ => throw new ArgumentOutOfRangeException(nameof(origin))
			};

			if (newPosition > this.EffectiveStream.Length && newPosition > this._thresholdInBytes)
			{
				this.SwapStream(newPosition);
			}

			return this.EffectiveStream.Seek(offset, origin);
		}

		public override void SetLength(long value)
		{
			if (value > this._thresholdInBytes)
			{
				this.SwapStream(value);
			}

			this.EffectiveStream.SetLength(value);
		}

#if NETCOREAPP2_1

		public override void CopyTo(Stream destination, int bufferSize)
			=> this.EffectiveStream.CopyTo(destination, bufferSize);

		public override int Read(Span<byte> buffer)
			=> this.EffectiveStream.Read(buffer);

		public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
			=> this.EffectiveStream.ReadAsync(buffer, cancellationToken);

		public override void Write(ReadOnlySpan<byte> buffer)
		{
			this.CheckInvariant();

			switch (this.QuerySwapNeeded(buffer.Length))
			{
				case SwapStatus.Required:
				{
					this.SwapStream(buffer.Length);
					goto case SwapStatus.AlreadySwapped;
				}
				case SwapStatus.AlreadySwapped:
				{
					this._bufferStream!.Write(buffer);
					return;
				}
			}

			this._backedStream!.Write(buffer);
		}

		public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
		{
			this.CheckInvariant();

			switch (this.QuerySwapNeeded(buffer.Length))
			{
				case SwapStatus.Required:
				{
					await this.SwapStreamAsync(buffer.Length, cancellationToken).ConfigureAwait(false);
					goto case SwapStatus.AlreadySwapped;
				}
				case SwapStatus.AlreadySwapped:
				{
					await this._bufferStream!.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
					return;
				}
			}

			await this._backedStream!.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
		}

#endif // NETCOREAPP2_1

		private enum SwapStatus
		{
			NotRequired = 0,
			AlreadySwapped,
			Required
		}
	}
}
