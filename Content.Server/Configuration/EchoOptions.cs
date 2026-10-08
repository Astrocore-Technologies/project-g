namespace Content.Server.Configuration;

public sealed class EchoOptions
{
    public const string SectionName = "Echoes";
    public bool Enabled { get; set; }
    public string DefinitionId { get; set; } = "test_guardian_echo";
    public string Name { get; set; } = "Мира";
    public float Speed { get; set; } = 5.5f;
    public float FollowDistance { get; set; } = 1.5f;
    public float DecisionSeconds { get; set; } = .2f;
    public float AssistSeconds { get; set; } = 4;
    public float LeashRadius { get; set; } = 7;
    public float AttackRange { get; set; } = 2;
    public double AttackPower { get; set; } = 8;
    public double AttackIntervalSeconds { get; set; } = 1.2;
    public float SignatureRange { get; set; } = 6;
    public float SignatureRadius { get; set; } = 2;
    public double SignaturePower { get; set; } = 24;
    public double SignatureCooldownSeconds { get; set; } = 8;
}
