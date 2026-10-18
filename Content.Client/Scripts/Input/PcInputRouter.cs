using Godot;

namespace ProjectG.Inputs;

public partial class PcInputRouter : Node
{
    public override void _Ready() => PcInput.Initialize();
}
