// Copyright (c) 2026 RedBolt and contributors.
// This work is licensed under the terms of the MIT license.
// For a copy, see <https://opensource.org/licenses/MIT> or the accompanying LICENSE file.

namespace RedBolt.XR;

using System;

using RedBolt.Core;

public static class XR
{
    /// <summary>Register XR library objects.</summary>
    public static void RegisterVRLibrary(Context context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.RegisterAssembly(typeof(XR).Assembly);
        if (context.GetSubsystem<VirtualReality>() is null)
            context.RegisterSubsystem<VirtualReality>(new OpenXR());
    }
}
