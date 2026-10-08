namespace Content.Server.Configuration;
public sealed class SocialOptions
{
    public const string SectionName="Social";
    public bool Enabled { get; set; }=true;
    public int MaxParties { get; set; }=64;
    public int MaxGuilds { get; set; }=32;
    public bool IsValid()=>MaxParties is >=1 and <=64 && MaxGuilds is >=1 and <=32;
}
