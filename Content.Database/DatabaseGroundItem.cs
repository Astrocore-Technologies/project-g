namespace Content.Database;

public sealed record DatabaseGroundItem(Guid InstanceId, string SeedId, string DefinitionId, float X, float Z)
{
    public void Validate()
    {
        if (InstanceId == Guid.Empty || string.IsNullOrWhiteSpace(SeedId) || SeedId.Length > 64 ||
            string.IsNullOrWhiteSpace(DefinitionId) || DefinitionId.Length > 64 || !float.IsFinite(X) || !float.IsFinite(Z))
            throw new InvalidDataException("Invalid ground item record.");
    }
}
