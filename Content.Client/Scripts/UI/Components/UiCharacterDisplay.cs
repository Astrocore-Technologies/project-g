using Content.Shared.Network;
using Godot;
namespace ProjectG.UI;

/// <summary>UI portrait and optional real visual preview. Equipment always reflects received inventory.</summary>
public partial class UiCharacterDisplay : Control
{
    private TextureRect _portrait=null!;
    private UiModelPreview _model=null!;
    private Button _switch=null!;
    private PackedScene? _appearance;
    private bool _loaded;
    private readonly Dictionary<EquipmentSlot,UiIconSlot> _slots=new();
    public override void _Ready()
    {
        _portrait=GetNode<TextureRect>("%Portrait"); _model=GetNode<UiModelPreview>("%Model"); _switch=GetNode<Button>("%SwitchView");
        _switch.Pressed+=SwitchView;
        foreach(var slot in new[]{EquipmentSlot.Armor,EquipmentSlot.Weapon,EquipmentSlot.Offhand})
        {
            var button=GetNode<UiIconSlot>("%"+slot); _slots.Add(slot,button);
            button.Bind("","Не экипировано"); button.Pressed+=()=>GameUi.Navigate("inventory");
        }
    }
    public void SetCharacter(PackedScene? appearance,Texture2D? portrait)
    {
        _appearance=appearance; _loaded=false; _portrait.Texture=portrait; _portrait.Visible=portrait is not null;
        _model.Visible=portrait is null && appearance is not null; _switch.Visible=appearance is not null && portrait is not null;
        if(_model.Visible) EnsureModel();
    }
    public void ApplyEquipment(InventoryState state)
    {
        foreach(var (slot,button) in _slots)
        {
            var item=state.Items?.FirstOrDefault(i=>i.Equipped && i.Slot==slot);
            button.Bind(item is { Handle:>0 } equipped?UiAssets.EquipmentKey(slot):"",item is { Handle:>0 } value?value.Name:"Не экипировано");
        }
    }
    private void EnsureModel() { if(_loaded || _appearance is null) return; _model.SetVisual(_appearance); _loaded=true; }
    private void SwitchView()
    {
        _model.Visible=!_model.Visible; _portrait.Visible=!_model.Visible;
        if(_model.Visible) EnsureModel(); _switch.Text=_model.Visible?"Иллюстрация":"3D-предпросмотр";
    }
    public override void _ExitTree() { _switch.Pressed-=SwitchView; }
}
