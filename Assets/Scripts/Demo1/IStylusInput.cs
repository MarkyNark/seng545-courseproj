using UnityEngine;

// Device-agnostic contract. Game logic never needs to know which controller is active.
public interface IStylusInput
{
    bool IsAvailable { get; }
    Vector3 WorldPosition { get; }
    bool GrabHeld { get; }
}
