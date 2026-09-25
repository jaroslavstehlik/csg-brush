namespace CsgBrush.Colliders
{
    /// <summary>What a brush's volume is to physics: solid geometry, a trigger volume, or no collider at all.</summary>
    public enum ColliderKind
    {
        Solid = 0,
        Trigger = 1,
        None = 2,
    }
}
