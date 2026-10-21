using Content.Server.Activities;
using Content.Server.Data;
using Content.Server.Progression;
using ProjectG.Balance;

if(args.Length is <2 or >3){Console.Error.WriteLine("Usage: CharacterStatsAudit <repository> <new-output-directory> [exported-input-directory]");return 2;}
var root=Path.GetFullPath(args[0]);var destination=Path.GetFullPath(args[1]);
if(Directory.Exists(destination)){Console.Error.WriteLine("Output directory must be new; existing evidence is preserved.");return 2;}
var content=args.Length==3?Path.Combine(args[2],"content.json"):Path.Combine(root,"Content.Server/Data/prototype.json");
var cases=args.Length==3?Path.Combine(args[2],"cases.json"):Path.Combine(root,"tools/CharacterStatsAudit/cases.json");
var rewards=args.Length==3?Path.Combine(args[2],"experience-rewards.json"):Path.Combine(root,"Content.Server/Data/experience-rewards.json");
var services=args.Length==3?Path.Combine(args[2],"camp-services.json"):Path.Combine(root,"Content.Server/Data/camp-services.json");
var report=CharacterStatsAudit.Run(ContentCatalog.Parse(File.ReadAllBytes(content)),
    ExperienceRewardsDefinition.LoadFile(rewards),CampServicesDefinition.LoadFile(services),
    CharacterStatsAudit.LoadCases(cases),CharacterStatsAudit.Hash(content),CharacterStatsAudit.Hash(cases));
CharacterStatsAudit.Export(report,destination);
foreach(var (source,name) in new[]{(content,"content.json"),(cases,"cases.json"),(rewards,"experience-rewards.json"),(services,"camp-services.json")})File.Copy(source,Path.Combine(destination,name));
File.WriteAllText(Path.Combine(destination,"environment.json"),System.Text.Json.JsonSerializer.Serialize(new{Runtime=Environment.Version.ToString(),OS=Environment.OSVersion.ToString(),Environment.ProcessorCount,CreatedUtc=DateTime.UtcNow},CharacterStatsAudit.Json));
Console.WriteLine($"CHARACTER_STATS_AUDIT_OK rows={report.Rows.Count}; cap={report.AlphaCap}; {destination}");
return 0;
