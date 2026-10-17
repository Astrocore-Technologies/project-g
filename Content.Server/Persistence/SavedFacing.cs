namespace Content.Server.Persistence;

/// <summary>Versioned server combat orientation; independent of animation bones and camera yaw.</summary>
public sealed record SavedFacing(int Version,float X,float Z)
{
    public void Validate()
    {
        if(Version!=1 || !float.IsFinite(X) || !float.IsFinite(Z) || Math.Abs((double)X*X+(double)Z*Z-1)>.0001)
            throw new InvalidDataException("Invalid saved combat orientation.");
    }
}
