using Content.Shared.Movement;
using Content.Shared.Network;

namespace Content.Server.Networking;

internal sealed class EchoSignatureIntentBuffer
{
    private readonly List<EchoSignatureCommand> _commands=new(NetworkConstants.MaxActiveEchoes);
    private uint _sequence;
    internal IReadOnlyList<EchoSignatureCommand> Commands=>_commands;
    internal void Add(EchoSignatureCommand command)
    {
        if(command.Sequence==0||!MovementSimulation.IsSequenceNewer(command.Sequence,_sequence))return;
        _sequence=command.Sequence;
        var index=_commands.FindIndex(c=>c.Slot==command.Slot);
        if(index>=0)_commands.RemoveAt(index);
        else if(_commands.Count>=NetworkConstants.MaxActiveEchoes)return;
        _commands.Add(command);
    }
}
