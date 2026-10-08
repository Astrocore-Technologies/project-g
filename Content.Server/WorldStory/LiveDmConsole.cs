using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
namespace Content.Server.WorldStory;
/// <summary>Same-process stdin tool; no admin UDP/HTTP endpoint or arbitrary script execution.</summary>
public sealed class LiveDmConsole(LiveDmInbox inbox,ILogger<LiveDmConsole> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken token)
    {
        if(!inbox.Enabled) return;
        logger.LogInformation("Live-DM console enabled. Prepared operations: OpenBridge, EstablishPatrol, StormRumor. Input: actor|credential|operation|reason");
        try
        {
            while(!token.IsCancellationRequested)
            {
                var line=await Task.Run(Console.ReadLine,CancellationToken.None).WaitAsync(token); if(line is null) return;
                var parts=line.Length<=1024 ? line.Split('|',4) : [];
                var accepted=parts.Length==4 && Enum.TryParse<LiveDmOperation>(parts[2],false,out var operation) && Enum.IsDefined(operation) && inbox.TrySubmit(parts[0],parts[1],operation,parts[3]);
                // Never print the submitted line, credentials or untrusted actor/reason.
                if(accepted) logger.LogInformation("Live-DM intention queued; completion is recorded by the world transaction.");
                else logger.LogWarning("Live-DM intention rejected (authorization, format or queue budget).");
            }
        }
        catch(OperationCanceledException) when(token.IsCancellationRequested) { }
    }
}
