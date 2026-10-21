using Godot;
namespace ProjectG.BalanceStudio;
public partial class Main
{
    private CharacterAuditWindow? _characterAudit;
    private void OpenCharacterAudit()
    {
        if(!CommitInputs())return;
        if(_characterAudit is null)
        {
            _characterAudit=GD.Load<PackedScene>("res://CharacterAuditWindow.tscn").Instantiate<CharacterAuditWindow>();
            _characterAudit.Theme=Theme;AddChild(_characterAudit);_characterAudit.Initialize(_repository,_document);
            _characterAudit.CatalogChanged+=()=>Changed(true);
        }
        _characterAudit.SetCatalog(_document);
        _characterAudit.Open();
    }
}
