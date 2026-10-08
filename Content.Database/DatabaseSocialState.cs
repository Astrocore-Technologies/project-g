namespace Content.Database;
public sealed record DatabaseSocialRow(string Key,byte Kind,long Revision,string State,string Name,Guid[] Members);
public sealed record DatabaseSocialSave(DatabaseWorldSave Realm,IReadOnlyList<DatabaseSocialRow> Rows);
internal static class SocialStorageValidation
{
    internal static void Rows(IReadOnlyList<DatabaseSocialRow> rows)
    {
        if(rows.Count>8192)throw new InvalidDataException("Social row budget exceeded.");var keys=new HashSet<string>();
        foreach(var r in rows){if(r.Key.Length is <1 or >48||!keys.Add(r.Key)||r.Kind>4||r.Revision<=0||System.Text.Encoding.UTF8.GetByteCount(r.Name)>96||r.Members.Length>32||r.Members.Any(id=>id==Guid.Empty)||r.Members.Distinct().Count()!=r.Members.Length||r.Kind is not (2 or 3)&&r.Members.Length!=0||r.Kind!=3&&r.Name.Length!=0)throw new InvalidDataException("Invalid social row.");StorageBounds.Document(r.State);}
    }
}
