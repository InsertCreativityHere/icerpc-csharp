// Copyright (c) ZeroC, Inc. All rights reserved.

module IceRpc::Tests::Slice
{
    struct TaggedExceptionStruct
    {
        s: string,
        v: int?,
    }

    exception TaggedException
    {
        mStruct: tag(50) TaggedExceptionStruct?,
        mInt: tag(1) int?,
        mBool: bool,
        mString: tag(2) string?,
    }

    exception TaggedExceptionPlus
    {
        mFloat: tag(3) float?,
        mStruct: tag(50) TaggedExceptionStruct?,
        mInt: tag(1) int?,
        mBool: bool,
        mString: tag(2) string?,
    }

    exception TaggedExceptionMinus
    {
        mBool: bool,
        mString: tag(2) string?,
        mStruct: tag(50) TaggedExceptionStruct?,
    }

    exception DerivedException : TaggedException
    {
        mString1: tag(600) string?,
        mStruct1: tag(601) TaggedExceptionStruct?,
    }

    exception RequiredException : TaggedException
    {
        mString1: string,
        mStruct1: TaggedExceptionStruct,
    }

    interface ExceptionTag
    {
        opTaggedException(p1: tag(1) int?, p2: tag(2) string?, p3: tag(3) TaggedExceptionStruct?);

        opDerivedException(p1: tag(1) int?, p2: tag(2) string?, p3: tag(3) TaggedExceptionStruct?);

        opRequiredException(p1: tag(1) int?, p2: tag(2) string?, p3: tag(3) TaggedExceptionStruct?);
    }
}
