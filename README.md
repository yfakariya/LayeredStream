# Layered Stream

[![Build status release](https://ci.appveyor.com/api/projects/status/TBD?svg=true)](https://ci.appveyor.com/project/yfakariya/layerd-stream)
[![Build status debug](https://ci.appveyor.com/api/projects/status/TBD?svg=true)](https://ci.appveyor.com/project/yfakariya/layerd-stream-TBD)

## What is it ?

This is a layered `System.IO.Stream` implementation for .NET.
Layered stream is usually `MemoryStream` backed by `FileStream` for large data support.

## Usage

As usual, you can use it with default constructor, which is backed by temp file:

```c#
using (var stream = new LayeredStream())
{
    ...
}
```

You can specify some options to optimize or change its behavior.

```c#
var options =
    new LayeredStreamOptions<string>
    {
        ThreasholdInBytes = 1024 * 1024,
        BackedStreamFactory =
            _ =>
            {
                var tempFilePath = Path.GetTempFileName();
                return new StreamInfo<string>(new FileStream(tempFilePath), tempFilePath);
            },
        BackedStreamCleaner =
            i =>
            {
                i.Stream.Dispose();
                File.Delete(i.State);
            },
        InMemoryStreamFactory = () => new StreamInfo(new RecyclableMemoryStream(manager), default(object)),
        InMemoryStreamCleaner = i => i.Stream.Dispose()
    };
using (var stream = new LayeredStream(in options))
{
    ...
}
```

## See also

* Issue tracker         : https://github.com/yfakariya/layered-stream/issues
