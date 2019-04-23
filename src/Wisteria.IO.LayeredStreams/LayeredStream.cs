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
		private readonly Func<StreamFactoryContext, StreamInfo> _backedStreamFactory;
		private readonly Action<StreamInfo> _backedStreamCleaner;
		private readonly bool _preferAsync;
		private Stream? _bufferStream;
		private object? _bufferStreamContext;
		private Stream? _backedStream;
		private object? _backedStreamContext;
		private bool _isDisposed;

		private Stream EffectiveStream => this._backedStream ?? this._bufferStream!;

		public override bool CanRead => !this._isDisposed;

		public override bool CanSeek => !this._isDisposed;

		public override bool CanWrite => !this._isDisposed;

		public override long Length
		{
			get
			{
				this.VerifyNotDisposed();
				return this.EffectiveStream.Length;
			}
		}

		public override long Position
		{
			get
			{
				this.VerifyNotDisposed();
				return this.EffectiveStream.Position;
			}
			set
			{
				this.VerifyNotDisposed();
				this.EffectiveStream.Position = value;
			}
		}

		public override bool CanTimeout => !this._isDisposed && this.EffectiveStream.CanTimeout;

		public override int ReadTimeout
		{
			get
			{
				this.VerifyNotDisposed();
				return this.EffectiveStream.ReadTimeout;
			}
			set
			{
				this.VerifyNotDisposed();
				this.EffectiveStream.ReadTimeout = value;
			}
		}

		public override int WriteTimeout
		{
			get
			{
				this.VerifyNotDisposed();
				return this.EffectiveStream.WriteTimeout;
			}
			set
			{
				this.VerifyNotDisposed();
				this.EffectiveStream.WriteTimeout = value;
			}
		}

		public LayeredStream() : this(default) { }

		public LayeredStream(in LayeredStreamOptions options)
		{
			ValidateOptions(in options);

			this._backedStreamFactory = options.BackedStreamFactory ?? this.CreateDefaultBackedStream;
			this._backedStreamCleaner = options.BackedStreamCleaner ?? CleanUpDefaultBackedStream;
			var bufferStreamInfo = (options.BufferStreamFactory ?? CreateDefaultBufferStream)(new StreamFactoryContext(options.InitialCapacity));
			ValidateStreamCapability(bufferStreamInfo.Stream, "buffer stream");
			this._bufferStreamContext = bufferStreamInfo.Context;
			this._bufferStream = bufferStreamInfo.Stream;
			this._bufferStreamCleaner = options.BufferStreamCleaner ?? CleanUpDefaultBufferStream;
			this._thresholdInBytes = options.ThresholdInBytes;
			this._preferAsync = options.PreferAsync;
		}

		private static void ValidateOptions(in LayeredStreamOptions options)
		{
			ValidateFactoryAndCleanerAreNotSymmetric(
				options.BufferStreamFactory,
				options.BufferStreamCleaner,
				nameof(options.BufferStreamFactory),
				nameof(options.BufferStreamCleaner)
			);
			ValidateFactoryAndCleanerAreNotSymmetric(
				options.BackedStreamFactory,
				options.BackedStreamCleaner,
				nameof(options.BackedStreamFactory),
				nameof(options.BackedStreamCleaner)
			);
		}

		private static void ValidateFactoryAndCleanerAreNotSymmetric(Func<StreamFactoryContext, StreamInfo>? factory, Action<StreamInfo>? cleaner, string factoryName, string cleanerName)
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
			=> throw new ArgumentException($"Both of {factoryName} and {cleanerName} are null or non-null.", "options");

		private StreamInfo CreateDefaultBackedStream(StreamFactoryContext context)
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

		private static StreamInfo CreateDefaultBufferStream(StreamFactoryContext context)
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

			this._isDisposed = true;

			base.Dispose(disposing);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private void VerifyNotDisposed()
		{
			if (this._isDisposed)
			{
				ThrowObjectDisposedException();
			}

			this.CheckInvariant();
		}

		private static void ThrowObjectDisposedException()
			=> throw new ObjectDisposedException(typeof(LayeredStream).FullName);

		[Conditional("DEBUG")]
		private void CheckInvariant()
			=> Debug.Assert(
				(this._bufferStream == null && this._backedStream != null) || (this._bufferStream != null && this._backedStream == null),
				"(this._bufferStream == null && this._backedStream != null) || (this._bufferStream != null && this._backedStream == null)"
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
			StreamInfo newStreamInfo = this._backedStreamFactory(context);
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
		{
			this.VerifyNotDisposed();
			this.EffectiveStream.Flush();
		}

		public override Task FlushAsync(CancellationToken cancellationToken)
		{
			this.VerifyNotDisposed();
			return this.EffectiveStream.FlushAsync(cancellationToken);
		}

		public override int Read(byte[] buffer, int offset, int count)
		{
			this.VerifyNotDisposed();
			return this.EffectiveStream.Read(buffer, offset, count);
		}

		public override int ReadByte()
		{
			this.VerifyNotDisposed();
			return this.EffectiveStream.ReadByte();
		}

		public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
		{
			this.VerifyNotDisposed();
			return this.EffectiveStream.ReadAsync(buffer, offset, count, cancellationToken);
		}

		public override void Write(byte[] buffer, int offset, int count)
		{
			this.VerifyNotDisposed();

			switch (this.QuerySwapNeeded(count))
			{
				case SwapStatus.Required:
				{
					this.SwapStream(count);
					break;
				}
				case SwapStatus.NotRequired:
				{
					this._bufferStream!.Write(buffer, offset, count);
					return;
				}
			}

			this._backedStream!.Write(buffer, offset, count);
		}

		public override void WriteByte(byte value)
		{
			this.VerifyNotDisposed();

			switch (this.QuerySwapNeeded(1))
			{
				case SwapStatus.Required:
				{
					this.SwapStream(1);
					break;
				}
				case SwapStatus.NotRequired:
				{
					this._bufferStream!.WriteByte(value);
					return;
				}
			}

			this._backedStream!.WriteByte(value);
		}

		public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
		{
			this.VerifyNotDisposed();

			switch (this.QuerySwapNeeded(count))
			{
				case SwapStatus.Required:
				{
					await this.SwapStreamAsync(count, cancellationToken).ConfigureAwait(false);
					break;
				}
				case SwapStatus.NotRequired:
				{
					await this._bufferStream!.WriteAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
					return;
				}
			}

			await this._backedStream!.WriteAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
		}

		public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
		{
			this.VerifyNotDisposed();
			return this.EffectiveStream.CopyToAsync(destination, bufferSize, cancellationToken);
		}

		public override long Seek(long offset, SeekOrigin origin)
		{
			this.VerifyNotDisposed();

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
			this.VerifyNotDisposed();

			if (value > this._thresholdInBytes)
			{
				this.SwapStream(value);
			}

			this.EffectiveStream.SetLength(value);
		}

#if NETCOREAPP2_1

		public override void CopyTo(Stream destination, int bufferSize)
		{
			this.VerifyNotDisposed();
			this.EffectiveStream.CopyTo(destination, bufferSize);
		}

		public override int Read(Span<byte> buffer)
		{
			this.VerifyNotDisposed();
			return this.EffectiveStream.Read(buffer);
		}

		public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
		{
			this.VerifyNotDisposed();
			return this.EffectiveStream.ReadAsync(buffer, cancellationToken);
		}

		public override void Write(ReadOnlySpan<byte> buffer)
		{
			this.VerifyNotDisposed();

			switch (this.QuerySwapNeeded(buffer.Length))
			{
				case SwapStatus.Required:
				{
					this.SwapStream(buffer.Length);
					break;
				}
				case SwapStatus.NotRequired:
				{
					this._bufferStream!.Write(buffer);
					return;
				}
			}

			this._backedStream!.Write(buffer);
		}

		public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
		{
			this.VerifyNotDisposed();

			switch (this.QuerySwapNeeded(buffer.Length))
			{
				case SwapStatus.Required:
				{
					await this.SwapStreamAsync(buffer.Length, cancellationToken).ConfigureAwait(false);
					break;
				}
				case SwapStatus.NotRequired:
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

#if DEBUG

		internal Endoscope DebugEndoscope => new Endoscope(this);

		internal readonly struct Endoscope
		{
			private readonly LayeredStream _enclosing;

			public Stream? BackedStream => this._enclosing._backedStream;
			public Action<StreamInfo> BackedStreamCleaner => this._enclosing._backedStreamCleaner;
			public object? BackedStreamContext => this._enclosing._backedStreamContext;
			public Func<StreamFactoryContext, StreamInfo> BackedStreamFactory => this._enclosing._backedStreamFactory;
			public Stream? BufferStream => this._enclosing._bufferStream;
			public Action<StreamInfo> BufferStreamCleaner => this._enclosing._bufferStreamCleaner;
			public object? BufferStreamContext => this._enclosing._bufferStreamContext;
			public Stream EffectiveStream => this._enclosing.EffectiveStream;

			internal Endoscope(LayeredStream enclosing)
			{
				this._enclosing = enclosing;
			}

			public void SwapStream(long newLength)
				=> this._enclosing.SwapStream(newLength);
		}
#endif // DEBUG
	}
}
