// Copyright (c) 2026 RedBolt and contributors.
// This work is licensed under the terms of the MIT license.
// For a copy, see <https://opensource.org/licenses/MIT> or the accompanying LICENSE file.

namespace RedBolt.XR;

public static class VREvents
{
    /// <summary>Paused VR session is being resumed, such as headset put back on.</summary>
    public struct VRResumeEvent;

    /// <summary>VR session is being paused, such as headset sensor reporting removed.</summary>
    public struct VRPauseEvent;

    /// <summary>An external source is terminating our VR instance.</summary>
    public struct VRExitEvent;

    /// <summary>Interaction profile has been changed, which means bindings have been remapped.</summary>
    public struct VRInteractionProfileChangedEvent;

    /// <summary>An input binding has been changed.</summary>
    public struct VRBindingChangedEvent
    {
        public XRBinding Binding;
    }

    /// <summary>Controller model has been changed.</summary>
    public struct VRControllerChangedEvent
    {
        public VRHand Hand;
    }
}
