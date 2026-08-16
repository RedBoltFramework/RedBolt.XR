// Copyright (c) 2026 RedBolt and contributors.
// This work is licensed under the terms of the MIT license.

namespace RedBolt.Sample.Android;

internal static class SampleCatalog
{
    internal static readonly SampleEntry[] Entries =
    [
        new("hello-vr", "Hello VR", static () => new global::RedBolt.Sample.HelloVR.HelloVRSample()),
    ];
}
