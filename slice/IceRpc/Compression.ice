// Copyright (c) ZeroC, Inc. All rights reserved.

module IceRpc
{
    /// The compression format of the Request or "Success" Response frame payload.
    unchecked enum CompressionFormat : byte
    {
        /// Reserved value which should not be encoded.
        NotCompressed = 0,

        /// The payload is compressed using the deflate format.
        Deflate = 1,
    }

    /// The compression field of the payload carried by a frame.
    [cs:readonly]
    struct CompressionField
    {
        /// The compression format.
        format: CompressionFormat,
    }
}
