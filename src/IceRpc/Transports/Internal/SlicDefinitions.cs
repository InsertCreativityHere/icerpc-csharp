// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Transports.Internal
{
    internal static class SlicDefinitions
    {
        // The header below is used to reserve space in the protocol frame to avoid allocating a separate byte
        // buffer. The Slic header is composed of a FrameType byte enum value, a FrameSize varuint32 value (4
        // bytes) and a stream ID varuint62 value (8 bytes).
        internal static readonly ReadOnlyMemory<byte> FrameHeader = new byte[13];

        internal const uint V1 = 1;
    }
}
