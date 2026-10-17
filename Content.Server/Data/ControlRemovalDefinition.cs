namespace Content.Server.Data;

/// <summary>Server-only removal permissions of one control instance, independent of its ACR category.</summary>
public sealed record ControlRemovalDefinition
{
    internal static readonly ControlRemovalDefinition Default=new();
    // Existing control profiles remain cleansable unless they explicitly author an exception.
    public bool Cleansable {get;init;}=true;
    public string[] Tags {get;init;}=[];

    public bool Equals(ControlRemovalDefinition? other)=>ReferenceEquals(this,other) || other is not null &&
        Cleansable==other.Cleansable && Tags is not null && other.Tags is not null && Tags.AsSpan().SequenceEqual(other.Tags);

    public override int GetHashCode()
    {
        var hash=new HashCode();hash.Add(Cleansable);
        if(Tags is not null)foreach(var tag in Tags)hash.Add(tag,StringComparer.Ordinal);
        return hash.ToHashCode();
    }

    internal void Validate()
    {
        EffectDefinition.ValidateTags(Tags);
        // Metadata travels in an individual persisted effect row, whose document is bounded to 8 KiB.
        if(Tags.Length>32)throw new ArgumentException("Control removal metadata exceeds its document budget.");
    }

    internal bool Matches(string[]? tags)
    {
        if(!Cleansable)return false;
        if(tags is null)return true;
        foreach(var tag in tags)if(Array.IndexOf(Tags,tag)>=0)return true;
        return false;
    }
}
