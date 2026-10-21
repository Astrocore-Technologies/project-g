using System.Security.Cryptography;
using System.Text.Json;
using Content.Server.Progression;

namespace ProjectG.Balance;

/// <summary>Validated reward draft with bounded history and optimistic disk conflict protection.</summary>
public sealed class ExperienceRewardDocument
{
    private readonly Stack<ExperienceRewardsDefinition> _undo=new(),_redo=new();
    private string _hash;private ExperienceRewardsDefinition _baseline;
    public string SourcePath {get;}
    public ExperienceRewardsDefinition Value {get;private set;}
    public bool Dirty=>Value!=_baseline;
    public bool CanUndo=>_undo.Count>0;public bool CanRedo=>_redo.Count>0;
    public ExperienceRewardDocument(string path)
    {SourcePath=Path.GetFullPath(path);Value=ExperienceRewardsDefinition.LoadFile(SourcePath);_baseline=Value;_hash=Hash();}
    public void Apply(ExperienceRewardsDefinition value)
    {
        value.Validate();if(value.Version!=Value.Version||value.Revision!=Value.Revision)throw new ArgumentException("Version/revision are managed when saving.");
        if(value==Value)return;if(_undo.Count==128)throw new InvalidOperationException("Save or reopen the draft before further edits.");
        _undo.Push(Value);_redo.Clear();Value=value;
    }
    public void Undo(){if(_undo.TryPop(out var old)){_redo.Push(Value);Value=old;}}
    public void Redo(){if(_redo.TryPop(out var next)){_undo.Push(Value);Value=next;}}
    public void Save()
    {
        if(Hash()!=_hash)throw new IOException("Файл наград изменён снаружи. Откройте актуальный файл; исходник сохранён.");
        if(!Dirty)return;
        var next=Value with {Revision=checked(_baseline.Revision+1)};next.Validate();
        BalanceDocument.WriteAtomic(SourcePath,JsonSerializer.SerializeToUtf8Bytes(next,new JsonSerializerOptions {WriteIndented=true,PropertyNamingPolicy=JsonNamingPolicy.CamelCase}));
        Value=next;_baseline=next;_hash=Hash();_undo.Clear();_redo.Clear();
    }
    private string Hash()=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(SourcePath)));
}
