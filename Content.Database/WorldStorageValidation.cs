namespace Content.Database;
internal static class WorldStorageValidation
{
    internal static void Batch(IReadOnlyList<DatabaseWorldSave> worlds, DatabaseSocialSave? social)
    {
        if (worlds.Count > 2) throw new InvalidDataException("Regional checkpoint exceeds the prototype budget.");
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var world in worlds)
        {
            Write(world);
            if (!keys.Add(world.Session.Key)) throw new InvalidDataException("Duplicate regional write.");
        }
        if (social is not null && !keys.Add(social.Realm.Session.Key))
            throw new InvalidDataException("Social and regional leases must be distinct.");
    }
    internal static void Key(string key)
    {
        if (key.Length is < 1 or > 64 || key.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_' && c != '-')) throw new InvalidDataException("Invalid world key.");
    }
    internal static void Write(DatabaseWorldSave write)
    {
        Key(write.Session.Key); StorageBounds.Document(write.State);
        if (write.ExpectedRevision < 0 || write.ExpectedRevision == long.MaxValue || write.Audit.Count is < 1 or > 128) throw new InvalidDataException("Invalid world write.");
        foreach (var a in write.Audit)
            if (string.IsNullOrWhiteSpace(a.Actor) || a.Actor.Length > 64 || string.IsNullOrWhiteSpace(a.Operation) || a.Operation.Length > 32 || string.IsNullOrWhiteSpace(a.Reason) || a.Reason.Length > 160 || a.Timestamp <= 0 || a.Actor.Any(char.IsControl) || a.Reason.Any(char.IsControl) || a.Operation.Any(char.IsControl)) throw new InvalidDataException("Invalid audit entry.");
    }
}
