using Godot;
using ProjectG.UI;

namespace ProjectG.Inputs;

public readonly record struct PcBinding(Key Key = Key.None, MouseButton Mouse = MouseButton.None, bool Ctrl = false, bool Shift = false, bool Alt = false)
{
    public bool Empty => Key == Key.None && Mouse == MouseButton.None;
    public InputEvent ToEvent() => Key != Key.None
        ? new InputEventKey { PhysicalKeycode = Key, CtrlPressed = Ctrl, ShiftPressed = Shift, AltPressed = Alt }
        : new InputEventMouseButton { ButtonIndex = Mouse, CtrlPressed = Ctrl, ShiftPressed = Shift, AltPressed = Alt };
    public static PcBinding? Capture(InputEvent input) => input switch
    {
        InputEventKey { Pressed: true, Echo: false } key => new(key.PhysicalKeycode != Key.None ? key.PhysicalKeycode : key.Keycode,
            Ctrl: key.CtrlPressed && key.PhysicalKeycode != Key.Ctrl, Shift: key.ShiftPressed && key.PhysicalKeycode != Key.Shift,
            Alt: key.AltPressed && key.PhysicalKeycode != Key.Alt),
        InputEventMouseButton { Pressed: true } mouse => new(Mouse: mouse.ButtonIndex, Ctrl: mouse.CtrlPressed, Shift: mouse.ShiftPressed, Alt: mouse.AltPressed),
        _ => null
    };
    public bool Matches(InputEvent input, bool pressed, bool modifiers = true)
    {
        if (Empty) return false;
        var button = input is InputEventKey key ? key.Pressed == pressed && !key.Echo && Key != Key.None &&
                (key.PhysicalKeycode != Key.None ? key.PhysicalKeycode : key.Keycode) == Key
            : input is InputEventMouseButton mouse && mouse.Pressed == pressed && Mouse != MouseButton.None && mouse.ButtonIndex == Mouse;
        if (!button || !modifiers) return button;
        var ev = (InputEventWithModifiers)input;
        return (ev.CtrlPressed && Key != Key.Ctrl) == Ctrl && (ev.ShiftPressed && Key != Key.Shift) == Shift && (ev.AltPressed && Key != Key.Alt) == Alt && !ev.MetaPressed;
    }
    public bool Held() => !Empty && (Key != Key.None ? Input.IsPhysicalKeyPressed(Key) : Input.IsMouseButtonPressed(Mouse)) &&
        (Input.IsPhysicalKeyPressed(Key.Ctrl) && Key != Key.Ctrl) == Ctrl &&
        (Input.IsPhysicalKeyPressed(Key.Shift) && Key != Key.Shift) == Shift &&
        (Input.IsPhysicalKeyPressed(Key.Alt) && Key != Key.Alt) == Alt;
    public override string ToString() => Empty ? "Не назначено" : (Ctrl ? "Ctrl + " : "") + (Shift ? "Shift + " : "") + (Alt ? "Alt + " : "") +
        (Key != Key.None ? OS.GetKeycodeString(Key) : Mouse switch { MouseButton.Left => "ЛКМ", MouseButton.Right => "ПКМ", MouseButton.Middle => "СКМ", MouseButton.WheelUp => "Колесо ↑", MouseButton.WheelDown => "Колесо ↓", _ => $"Мышь {(int)Mouse}" });
}

public sealed record PcAction(string Id, string Title, PcBinding Default);

/// <summary>One physical-input/profile boundary. Presenters consume named actions, never fixed hardware keys.</summary>
public static class PcInput
{
    public const string Move = "move", Basic = "basic", Manual = "manual", Dodge = "dodge", Block = "block", Parry = "parry", Cancel = "cancel", Stop = "stop", Context = "context", Follow = "follow";
    public static readonly string[] Skills = Enumerable.Range(1, 8).Select(i => $"skill_{i}").ToArray();
    public static readonly string[] Echoes = ["echo_1", "echo_2", "echo_3"];
    public static readonly PcAction[] Actions = BuildActions();
    private static readonly Dictionary<string, PcBinding> Bindings = new(StringComparer.Ordinal);
    private static bool _loaded;
    public static string ProfilePath { get; private set; } = "user://settings/input.cfg";
    public static bool Capturing { get; set; }
    public static bool ContextOpen { get; set; }
    public static event Action? Changed;
    private static PcAction[] BuildActions()
    {
        var values = new List<PcAction> {
            new(Move,"Движение",new(Mouse:MouseButton.Right)), new(Basic,"Базовая атака / удержание",new(Mouse:MouseButton.Left)),
            new(Manual,"Одиночная базовая атака",new(Mouse:MouseButton.Left,Ctrl:true)),
            new(Dodge,"Рывок / быстрое восстановление",new(Key.Space)), new(Block,"Блок",new(Key.Tab)), new(Parry,"Парирование",new(Key.Shift)),
            new(Cancel,"Отмена действия / прицеливания",new(Key.Escape)), new(Stop,"Остановить движение / Follow",new()),
            new(Context,"Меню игрока",new(Mouse:MouseButton.Right)), new(Follow,"Следовать за игроком под курсором",new()),
            new("zoom_in","Приблизить камеру",new(Mouse:MouseButton.WheelUp)), new("zoom_out","Отдалить камеру",new(Mouse:MouseButton.WheelDown)) };
        Key[] keys = [Key.Q,Key.W,Key.E,Key.R,Key.A,Key.S,Key.D,Key.F];
        for(var i=0;i<8;i++) values.Add(new(Skills[i],$"Навык {i+1}",new(keys[i])));
        for(var i=0;i<3;i++) values.Add(new(Echoes[i],$"Команда Эхо {i+1}",new(new[]{Key.T,Key.Y,Key.U}[i])));
        (string Id,string Title,Key Key)[] windows = [("character","Персонаж",Key.K),("inventory","Инвентарь",Key.I),("echo","Эхо",Key.F3),
            ("map","Карта",Key.M),("journal","Журнал",Key.L),("profession","Профессия",Key.P),("craft","Ремесло",Key.C),
            ("trade","Обмен",Key.B),("market","Рынок",Key.J),("party","Группа",Key.N),("clan","Гильдия игроков",Key.O),
            ("pvp","PvP",Key.V),("keeper","Хранитель переправы",Key.H),("guide","Памятка",Key.F1),("emote","Жесты",Key.F4),
            ("talk","Разговор",Key.F2),("pickup","Подобрать предмет",Key.G),("menu","Меню",Key.Escape)];
        foreach(var window in windows) values.Add(new(window.Id,window.Title,new(window.Key)));
        values.Add(new("guild","Гильдия авантюристов",new(Key.G,Ctrl:true)));
        return values.ToArray();
    }
    public static void Initialize(string? path = null)
    {
        if (_loaded && path is null) return;
        ProfilePath = path ?? ProfilePath; _loaded = true; Bindings.Clear();
        foreach(var action in Actions) Bindings[action.Id] = action.Default;
        using var config = new ConfigFile();
        if(config.Load(ProfilePath)==Error.Ok)
            foreach(var action in Actions)
            {
                if(!config.HasSectionKey("bindings",action.Id))continue;
                var value = config.GetValue("bindings",action.Id);
                if(value.VariantType!=Variant.Type.PackedInt64Array) continue;
                var data=value.AsInt64Array();
                if(data.Length!=5 || data[0]<0 || data[0]>uint.MaxValue || data[1]<0 || data[1]>9 || data.Skip(2).Any(n=>n is <0 or >1) || data[0]!=0 && data[1]!=0)continue;
                var binding=new PcBinding((Key)data[0],(MouseButton)data[1],data[2]!=0,data[3]!=0,data[4]!=0);
                if(Enum.IsDefined(binding.Key))Bindings[action.Id]=binding;
            }
        if(Bindings.Any(pair=>Conflict(pair.Key,pair.Value,out _)))
            foreach(var action in Actions)Bindings[action.Id]=action.Default;
        InstallActions(); Changed?.Invoke();
    }
    private static void InstallActions()
    {
        foreach(var action in Actions)
        {
            var name="pc_"+action.Id;
            if(!InputMap.HasAction(name)) InputMap.AddAction(name);
            InputMap.ActionEraseEvents(name);
            if(!Bindings[action.Id].Empty)InputMap.ActionAddEvent(name,Bindings[action.Id].ToEvent());
        }
    }
    private static bool Sharing(string a,string b) => (a is Move or Context && b is Move or Context) || (a is Cancel or "menu" && b is Cancel or "menu");
    private static bool Conflict(string action,PcBinding binding,out string reason)
    {
        foreach(var pair in Bindings)
            if(pair.Key!=action && !binding.Empty && pair.Value==binding && !Sharing(action,pair.Key))
            {reason=$"Кнопка занята: {Actions.First(a=>a.Id==pair.Key).Title}";return true;}
        reason="";return false;
    }
    public static bool Assign(string action,PcBinding binding,out string reason)
    {
        Initialize();
        if(!Bindings.ContainsKey(action)){reason="Неизвестное действие";return false;}
        if(binding.Mouse is MouseButton.WheelUp or MouseButton.WheelDown &&
            (action is Move or Basic or Manual or Block or Dodge || Skills.Contains(action) || Echoes.Contains(action)))
        {reason="Для этого действия нужна кнопка, которую можно удерживать";return false;}
        if(Conflict(action,binding,out reason))return false;
        var old=Bindings[action];Bindings[action]=binding;
        if(!Save()){Bindings[action]=old;reason="Не удалось сохранить управление";return false;}
        InstallActions(); Changed?.Invoke();return true;
    }
    public static bool Reset(out string reason)
    {
        Initialize();var old=new Dictionary<string,PcBinding>(Bindings);
        foreach(var action in Actions) Bindings[action.Id]=action.Default;
        if(!Save()){Bindings.Clear();foreach(var pair in old)Bindings.Add(pair.Key,pair.Value);reason="Не удалось сохранить управление";return false;}
        InstallActions();Changed?.Invoke();reason="";return true;
    }
    private static bool Save()
    {
        if(DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(ProfilePath.GetBaseDir()))!=Error.Ok)return false;
        using var config=new ConfigFile();
        foreach(var pair in Bindings)config.SetValue("bindings",pair.Key,new long[]{(long)pair.Value.Key,(long)pair.Value.Mouse,pair.Value.Ctrl?1:0,pair.Value.Shift?1:0,pair.Value.Alt?1:0});
        return config.Save(ProfilePath)==Error.Ok;
    }
    public static PcBinding Binding(string action){Initialize();return Bindings[action];}
    public static string Label(string action)=>Binding(action).ToString();
    public static bool Pressed(string action,InputEvent input)=>!Capturing&&Binding(action).Matches(input,true);
    public static bool Released(string action,InputEvent input)=>Binding(action).Matches(input,false,modifiers:false);
    public static bool Held(string action)=>!Capturing&&Binding(action).Held();
    public static int SkillIndex(InputEvent input){for(var i=0;i<Skills.Length;i++)if(Pressed(Skills[i],input))return i;return -1;}
    public static int EchoIndex(InputEvent input){for(var i=0;i<Echoes.Length;i++)if(Pressed(Echoes[i],input))return i+1;return 0;}
    public static string? WindowAction(Key key)=>key switch {Key.K=>"character",Key.I=>"inventory",Key.F3=>"echo",Key.M=>"map",Key.L=>"journal",Key.P=>"profession",Key.C=>"craft",Key.B=>"trade",Key.J=>"market",Key.N=>"party",Key.O=>"clan",Key.V=>"pvp",Key.H=>"keeper",Key.F1=>"guide",Key.F4=>"emote",Key.F2=>"talk",Key.G=>"guild",Key.Escape=>"menu",_=>null};
    public static bool WindowPressed(Key original,InputEvent input)=>WindowAction(original) is {} action&&Pressed(action,input);
    public static bool Blocked(Node owner)=>Capturing||ContextOpen||GameUi.GameplayModalOpen||owner.GetViewport().GuiGetFocusOwner() is LineEdit or TextEdit;
}
