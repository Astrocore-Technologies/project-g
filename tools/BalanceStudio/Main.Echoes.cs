using Content.Server.Data;
using Godot;

namespace ProjectG.BalanceStudio;

public partial class Main
{
    private void DrawEchoRefusal(VBoxContainer parent)
    {
        var path=_page.RootPath;var node=_document.Read(path)!;
        var card=Card(parent,"Редкость и боевой отказ",
            "Отказ доступен только 5★. Причины и срок задаёт система отношений; здесь явно выбирается реакция на уже начатое действие.",
            ()=>Run(()=>{_document.ResetPaths([path+"/stars",path+"/refusalDuringAction"]);Changed(true);}));
        var stars=new OptionButton{Name="EchoStars"};card.AddChild(stars);stars.AddItem("Редкость не задана",0);
        for(var i=1;i<=5;i++)stars.AddItem(i+"★",i);
        stars.Select(node["stars"]?.GetValue<int>()??0);
        var reaction=new OptionButton{Name="EchoRefusalReaction"};card.AddChild(reaction);
        reaction.AddItem("Явно выберите реакцию для 5★",0);
        reaction.AddItem("Завершить начатое действие",1);
        reaction.AddItem("Прервать по общим правилам прерывания",2);
        reaction.Select(node["refusalDuringAction"]?.GetValue<string>() switch
        {nameof(EchoRefusalActionReaction.FinishCurrent)=>1,nameof(EchoRefusalActionReaction.InterruptAsEnemy)=>2,_=>0});
        var apply=Button("Применить редкость и реакцию",()=>Run(()=>
        {
            var rarity=stars.GetSelectedId();var chosen=reaction.GetSelectedId();
            EchoRefusalActionReaction? selected=rarity==5?chosen switch
            {1=>EchoRefusalActionReaction.FinishCurrent,2=>EchoRefusalActionReaction.InterruptAsEnemy,_=>null}:null;
            _document.ConfigureEchoRefusal(path,rarity==0?null:(byte)rarity,selected);
            Changed(true);
        }));apply.Name="EchoRefusalApply";card.AddChild(apply);
        void Refresh(){reaction.Disabled=stars.GetSelectedId()!=5;apply.Disabled=stars.GetSelectedId()==5&&reaction.GetSelectedId()==0;}
        stars.ItemSelected+=_=>Refresh();reaction.ItemSelected+=_=>Refresh();Refresh();
    }
}
