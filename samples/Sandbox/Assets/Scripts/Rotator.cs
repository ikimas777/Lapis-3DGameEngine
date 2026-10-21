using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Lapis;

/// <summary>Spins the entity at a constant speed.</summary>
public class Rotator : Behaviour
{
    [Tooltip("Degrees per second around the X, Y and Z axes.")]
    public Vector3 degreesPerSecond = new(0, 45, 0);

    public override void Update()
    {
        transform.LocalRotation *= MathUtil.FromEulerDegrees(degreesPerSecond * Time.DeltaTime);
    }
}
