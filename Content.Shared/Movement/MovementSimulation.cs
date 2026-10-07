using System.Numerics;

namespace Content.Shared.Movement;

/// <summary>
/// Shared fixed-step movement math. The server remains authoritative;
/// clients use the same function only to predict responsive motion.
/// </summary>
public static class MovementSimulation
{
    public static Vector2 Step(
        Vector2 position,
        Vector2 target,
        MovementSettings settings,
        float deltaSeconds)
    {
        if (!IsFinite(position) ||
            !IsValidTarget(target, settings) ||
            !float.IsFinite(deltaSeconds) ||
            deltaSeconds <= 0f)
        {
            return position;
        }

        var offset = target - position;
        var distance = offset.Length();

        if (distance <= settings.StopDistance)
            return target;

        var step = settings.Speed * deltaSeconds;
        if (distance <= step)
            return target;

        return position + offset / distance * step;
    }

    public static bool IsValidTarget(Vector2 target, MovementSettings settings)
    {
        return IsFinite(target) &&
               target.X >= settings.MinX &&
               target.X <= settings.MaxX &&
               target.Y >= settings.MinZ &&
               target.Y <= settings.MaxZ;
    }

    public static Vector2 ClampTarget(Vector2 target, MovementSettings settings)
    {
        return new Vector2(
            Math.Clamp(target.X, settings.MinX, settings.MaxX),
            Math.Clamp(target.Y, settings.MinZ, settings.MaxZ));
    }

    public static bool IsSequenceNewer(uint candidate, uint current)
    {
        return unchecked((int) (candidate - current)) > 0;
    }

    private static bool IsFinite(Vector2 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y);
}
