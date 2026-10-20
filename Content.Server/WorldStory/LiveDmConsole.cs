using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
namespace Content.Server.WorldStory;
/// <summary>Same-process stdin tool; no admin UDP/HTTP endpoint or arbitrary script execution.</summary>
public sealed class LiveDmConsole(LiveDmInbox inbox,ILogger<LiveDmConsole> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken token)
    {
        if(!inbox.Enabled) return;
        logger.LogInformation("Live-DM console enabled. Prepared operations: OpenBridge, EstablishPatrol, StormRumor, StartCampWatch. Input: actor|credential|operation|reason[|region]");
        try
        {
            while(!token.IsCancellationRequested)
            {
                var line=await Task.Run(Console.ReadLine,CancellationToken.None).WaitAsync(token); if(line is null) return;
                var parts=line.Length<=1024 ? line.Split('|',5) : [];
                var accepted=parts.Length is 4 or 5 && Enum.TryParse<LiveDmOperation>(parts[2],false,out var operation) && Enum.IsDefined(operation) && inbox.TrySubmit(parts[0],parts[1],operation,parts[3],parts.Length==5?parts[4]:null);
                // Never print the submitted line, credentials or untrusted actor/reason.
                if(accepted) logger.LogInformation("Live-DM intention queued; completion is recorded by the world transaction.");
                else logger.LogWarning("Live-DM intention rejected (authorization, format or queue budget).");
            }
        }
        catch(OperationCanceledException) when(token.IsCancellationRequested) { }
    }
}
