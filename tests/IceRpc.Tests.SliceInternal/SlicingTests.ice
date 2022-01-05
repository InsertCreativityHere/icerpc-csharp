// Copyright (c) ZeroC, Inc. All rights reserved.

module IceRpc::Tests::SliceInternal
{
    class MyBaseClass
    {
        m1: string,
    }

    class MyDerivedClass : MyBaseClass
    {
        m2: string,
    }

    class MyMostDerivedClass : MyDerivedClass
    {
        m3: string,
    }

    class MyCompactBaseClass(1)
    {
        m1: string,
    }

    class MyCompactDerivedClass(2) : MyCompactBaseClass
    {
        m2: string,
    }

    class MyCompactMostDerivedClass(3) : MyCompactDerivedClass
    {
        m3: string,
    }

    exception MyBaseException
    {
        m1: string,
    }

    exception MyDerivedException : MyBaseException
    {
        m2: string,
    }

    exception MyMostDerivedException : MyDerivedException
    {
        m3: string,
    }

    [preserve-slice]
    class MyPreservedClass : MyBaseClass
    {
        m2: string,
    }

    class MyPreservedDerivedClass1 : MyPreservedClass
    {
        m3: MyBaseClass,
    }

    class MyPreservedDerivedClass2(56) : MyPreservedClass
    {
        m3: MyBaseClass,
    }
}
