using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Options;
namespace Content.Server.WorldStory;
public enum LiveDmOperation { OpenBridge, EstablishPatrol, StormRumor }
internal sealed record AuthorizedLiveDm(string Actor,LiveDmOperation Operation,string Reason);
public sealed class LiveDmOptions
{
    public const string SectionName="LiveDm";
    public bool Enabled { get; init; }
    public Dictionary<string,string> Operators { get; init; }=new();
}
/// <summary>Disabled by default. Secret hashes are deployment configuration, never content or wire data.</summary>
public sealed class LiveDmInbox
{
    private readonly LiveDmOptions _options;
    private readonly Channel<AuthorizedLiveDm> _commands=Channel.CreateBounded<AuthorizedLiveDm>(new BoundedChannelOptions(8) { SingleReader=true,FullMode=BoundedChannelFullMode.Wait });
    public bool Enabled => _options.Enabled;
    public LiveDmInbox(IOptions<LiveDmOptions> options)
    {
        _options=options.Value;
        if(_options.Operators.Count>8 || _options.Operators.Any(o=>o.Key.Length is < 1 or > 32 || o.Key.Any(c=>!char.IsAsciiLetterOrDigit(c) && c!='_' && c!='-') || o.Value.Length!=64 || !o.Value.All(char.IsAsciiHexDigit)) || _options.Enabled && _options.Operators.Count==0) throw new ArgumentException("Invalid Live-DM authorization configuration.");
    }
    public bool TrySubmit(string actor,string credential,LiveDmOperation operation,string reason)
    {
        if(!_options.Enabled || actor is null || !_options.Operators.TryGetValue(actor,out var hash) || credential is null || credential.Length!=64 || !credential.All(char.IsAsciiHexDigit) || !Enum.IsDefined(operation) || string.IsNullOrWhiteSpace(reason) || reason.Length>160 || reason.Any(char.IsControl)) return false;
        var digest=SHA256.HashData(Encoding.ASCII.GetBytes(credential));
        return CryptographicOperations.FixedTimeEquals(digest,Convert.FromHexString(hash)) && _commands.Writer.TryWrite(new(actor,operation,reason));
    }
    internal bool TryTake(out AuthorizedLiveDm command) => _commands.Reader.TryRead(out command!);
}
