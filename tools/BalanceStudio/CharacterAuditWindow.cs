using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using Content.Server.Activities;
using Godot;
using ProjectG.Balance;

namespace ProjectG.BalanceStudio;

/// <summary>Editable tool scene: binds separate reward drafts and the existing catalog draft.</summary>
public partial class CharacterAuditWindow:Control
{
    private BalanceDocument _catalog=null!;private ExperienceRewardDocument _rewards=null!;
    private string _repository="";private bool _refreshing;
    private Label _status=null!;private Button _undo=null!,_redo=null!;
    private readonly Dictionary<string,SpinBox> _fields=new();
    public bool HasUnsavedChanges=>_rewards?.Dirty==true||_rewards is not null&&_fields.Count==6&&FieldsDiffer();
    public ExperienceRewardDocument RewardDraft=>_rewards;
    public event Action? CatalogChanged;
    public void SetCatalog(BalanceDocument catalog)=>_catalog=catalog;
    public void Initialize(string repository,BalanceDocument catalog,string? rewardPath=null)
    {
        _repository=repository;_catalog=catalog;_rewards=new(rewardPath??Path.Combine(repository,"Content.Server/Data/experience-rewards.json"));
        Refresh();
    }
    public override void _Ready()
    {
        _status=GetNode<Label>("%Status");_undo=GetNode<Button>("%Undo");_redo=GetNode<Button>("%Redo");
        foreach(var name in new[]{"Pve","Radius","Cells","Exploration","Ore","Watch"})
        {
            _fields[name]=GetNode<SpinBox>("%"+name);
            _fields[name].ValueChanged+=_=>{if(!_refreshing&&_rewards is not null)_status.Text="Поля изменены. Внесите их в черновик, сохраните или восстановите поля из черновика.";};
        }
        GetNode<Button>("%ResetFields").Pressed+=Refresh;
        var reload=GetNode<ConfirmationDialog>("ReloadConfirmation");
        void Reload(){_rewards=new(_rewards.SourcePath);Refresh();}
        reload.Confirmed+=()=>Guard(Reload);
        GetNode<Button>("%Reload").Pressed+=()=>{if(HasUnsavedChanges)reload.PopupCentered();else Guard(Reload);};
        GetNode<Button>("%ApplyRewards").Pressed+=()=>Guard(ApplyRewards);
        _undo.Pressed+=()=>Guard(()=>{_rewards.Undo();Refresh();});_redo.Pressed+=()=>Guard(()=>{_rewards.Redo();Refresh();});
        GetNode<Button>("%SaveRewards").Pressed+=()=>Guard(()=>{ApplyRewards();_rewards.Save();Refresh();_status.Text="Награды сохранены. Сервер применит их после перезапуска; EXP-кривая сохраняется в основном черновике отдельно.";});
        GetNode<Button>("%ApplyCurve").Pressed+=()=>Guard(()=>
        {
            if(!GetNode<CheckBox>("%Experimental").ButtonPressed)throw new InvalidOperationException("Отметьте экспериментальную конфигурацию.");
            _catalog.ConfigurePowerExperienceCurve(GetNode<SpinBox>("%Scale").Value,GetNode<SpinBox>("%Offset").Value);
            CatalogChanged?.Invoke();
            _status.Text="Экспериментальная кривая внесена в основной черновик. Ctrl+Z / сохранение — в основном окне. Рабочий файл ещё не изменён.";
        });
        GetNode<Button>("%Export").Pressed+=()=>Guard(()=>{ApplyRewards();_status.Text="Отчёт и точные входные файлы: "+Export();});
        GetNode<Button>("%Close").Pressed+=Hide;
    }
    public void Open(){Show();GetNode<Button>("%Close").GrabFocus();}
    public override void _UnhandledKeyInput(InputEvent input)
    {if(!IsVisibleInTree()||input is not InputEventKey {Pressed:true} key)return;if(key.Keycode==Key.Escape)Hide();GetViewport().SetInputAsHandled();}
    public void ApplyRewards()
    {
        if(_refreshing)return;
        _rewards.Apply(_rewards.Value with {PveExperience=(int)_fields["Pve"].Value,PveRadius=(float)_fields["Radius"].Value,
            ExplorationCells=(int)_fields["Cells"].Value,ExplorationExperience=(int)_fields["Exploration"].Value,
            OreExperience=(int)_fields["Ore"].Value,WatchExperience=(int)_fields["Watch"].Value});Refresh();
    }
    private bool FieldsDiffer()
    {
        var r=_rewards.Value;
        return _fields["Pve"].Value!=r.PveExperience||Math.Abs(_fields["Radius"].Value-r.PveRadius)>.0001||_fields["Cells"].Value!=r.ExplorationCells||
            _fields["Exploration"].Value!=r.ExplorationExperience||_fields["Ore"].Value!=r.OreExperience||_fields["Watch"].Value!=r.WatchExperience;
    }
    private void Refresh()
    {
        if(_rewards is null||_status is null)return;_refreshing=true;var r=_rewards.Value;
        double[] values=[r.PveExperience,r.PveRadius,r.ExplorationCells,r.ExplorationExperience,r.OreExperience,r.WatchExperience];
        var index=0;foreach(var field in _fields.Values)field.Value=values[index++];
        _undo.Disabled=!_rewards.CanUndo;_redo.Disabled=!_rewards.CanRedo;
        _status.Text=$"{_rewards.SourcePath}\nВерсия наград {r.Revision} · {(_rewards.Dirty?"есть несохранённые изменения":"без изменений")}. Закрытие окна сохраняет черновик в памяти.";
        _refreshing=false;
    }
    public string Export()
    {
        var output=Path.Combine(_repository,".artifacts/character-stats/c7-"+Guid.NewGuid().ToString("N"));
        var casesPath=Path.Combine(_repository,"tools/CharacterStatsAudit/cases.json");var servicePath=Path.Combine(_repository,"Content.Server/Data/camp-services.json");
        var content=Encoding.UTF8.GetBytes(_catalog.Text);
        var report=CharacterStatsAudit.Run(_catalog.Catalog,_rewards.Value,CampServicesDefinition.LoadFile(servicePath),CharacterStatsAudit.LoadCases(casesPath),
            Convert.ToHexString(SHA256.HashData(content)),CharacterStatsAudit.Hash(casesPath));
        CharacterStatsAudit.Export(report,output);
        BalanceDocument.WriteAtomic(Path.Combine(output,"content.json"),content);
        BalanceDocument.WriteAtomic(Path.Combine(output,"experience-rewards.json"),JsonSerializer.SerializeToUtf8Bytes(_rewards.Value,new JsonSerializerOptions{WriteIndented=true,PropertyNamingPolicy=JsonNamingPolicy.CamelCase}));
        File.Copy(casesPath,Path.Combine(output,"cases.json"));File.Copy(servicePath,Path.Combine(output,"camp-services.json"));
        return output;
    }
    private void Guard(Action action){try{action();}catch(Exception error){_status.Text=error.Message;}}
}
