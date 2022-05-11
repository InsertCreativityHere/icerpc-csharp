// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice.Internal;
using NUnit.Framework;

namespace IceRpc.Slice.Tests;

[Parallelizable(scope: ParallelScope.All)]
public class Slice1NullableTests
{
    [Test]
    public void Using_null_for_non_nullable_proxy_fails_during_decoding()
    {
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice1);
        encoder.EncodeNullableProxy(null);

        Assert.That(
            () =>
            {
                var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice1);
                decoder.DecodeProxy();
            },
            Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void Using_null_for_non_nullable_class_fails_during_decoding()
    {
        var buffer = new MemoryBufferWriter(new byte[256]);
        var encoder = new SliceEncoder(buffer, SliceEncoding.Slice1);
        encoder.EncodeNullableClass(null);

        Assert.That(
            () =>
            {
                var decoder = new SliceDecoder(buffer.WrittenMemory, SliceEncoding.Slice1);
                var decoded = decoder.DecodeClass<AnyClass>();
            },
            Throws.TypeOf<InvalidDataException>());
    }
}