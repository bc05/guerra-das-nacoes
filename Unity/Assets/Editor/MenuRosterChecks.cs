using System;
using System.IO;
using System.Linq;
using GuerraDasNacoes;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

public static class MenuRosterChecks
{
    private static int assertions;
    [MenuItem("Guerra das Nações/Validar lista de personagens")]
    public static void Run()
    {
        assertions = 0;
        string directory = Path.Combine(Path.GetTempPath(), "gdn-menu-roster-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            CheckSecondLauncher(directory);
            CheckPendingNicknames(directory);
            CharacterRoster roster = CharacterRosterStore.Load(directory);
            Check(roster.entries.Length == 1 && roster.entries[0].profile == "principal", "menu importa o perfil principal conhecido");
            Check(!OnlineIdentityStore.TryLoadExisting(directory, "principal", out _), "abrir o menu não cria identidade");
            Check(!Directory.Exists(Path.Combine(directory,"Online","principal")), "prévia sem identidade não cria pasta do personagem");
            OnlineIdentity old = OnlineIdentityStore.LoadOrCreate(directory,"principal");
            string original = File.ReadAllText(OnlineIdentityStore.GetIdentityPath(directory,"principal"));
            OnlineIdentityStore.LoadOrCreate(directory,"segundo");
            OnlineIdentityStore.LoadOrCreate(directory,"check-random-not-a-player");
            roster = CharacterRosterStore.Load(directory);
            Check(roster.entries.Length == 2 && roster.entries.Any(e=>e.profile=="segundo"), "menu importa o segundo personagem do atalho conhecido");
            Check(!roster.entries.Any(e=>e.profile=="check-random-not-a-player"), "perfis de teste não aparecem na seleção");
            CharacterRosterEntry added = CharacterRosterStore.AddCharacter(directory,roster,"  Guardião Azul  ");
            Check(added.displayName == "Guardião Azul" && added.profile.StartsWith("h-",StringComparison.Ordinal), "nome e perfil do novo personagem são independentes");
            Check(!OnlineIdentityStore.TryLoadExisting(directory,added.profile,out _), "criar card não cria credencial antes de conectar");
            string rosterPath = Path.Combine(directory,"Online","characters.json");
            string serialized = File.ReadAllText(rosterPath);
            Check(!serialized.Contains("deviceKey") && !serialized.Contains(old.deviceKey), "lista não armazena credenciais");
            Check(CharacterRosterStore.Load(directory).entries.Length == 3, "reabrir lista não duplica personagens");
            Check(File.ReadAllText(OnlineIdentityStore.GetIdentityPath(directory,"principal"))==original, "menu preserva a identidade principal byte a byte");
            Reject<ArgumentException>(()=>CharacterRosterStore.AddCharacter(directory,roster," "),"nome vazio recusado");
            Reject<ArgumentException>(()=>CharacterRosterStore.AddCharacter(directory,roster,"<invasor>"),"nome com marcação recusado");
            Reject<ArgumentException>(()=>CharacterRosterStore.AddCharacter(directory,roster,"A\nB"),"nome com controle recusado");
            Reject<ArgumentException>(()=>CharacterRosterStore.AddCharacter(directory,roster,new string('x',25)),"nome longo recusado");
            Check(File.ReadAllText(rosterPath)==serialized,"tentativas inválidas preservam o arquivo");
            while(roster.entries.Length<CharacterRosterStore.MaximumCharacters)
                CharacterRosterStore.AddCharacter(directory,roster,"Guerreiro "+roster.entries.Length);
            Reject<InvalidOperationException>(()=>CharacterRosterStore.AddCharacter(directory,roster,"Excedente"),"limite de personagens não é ultrapassado");
            Check(roster.entries.Select(e=>e.profile).Distinct().Count()==8,"cada personagem tem perfil exclusivo");
            string local = OnlineIdentityStore.GetIdentityRoot(directory,"http://127.0.0.1:8088");
            string remote = OnlineIdentityStore.GetIdentityRoot(directory,"https://game.example:8443");
            string another = OnlineIdentityStore.GetIdentityRoot(directory,"https://other.example:8443");
            Check(local==directory && remote!=directory && remote!=another,"credencial local preservada e servidores remotos separados");
            Check(remote==OnlineIdentityStore.GetIdentityRoot(directory,"https://GAME.example:8443/"),"origem normalizada produz namespace estável");
            Reject<ArgumentException>(()=>OnlineIdentityStore.GetIdentityRoot(directory,"https://user:secret@game.example"),"origem com credencial recusada");
            File.WriteAllText(rosterPath,"{ invalid");
            Reject<InvalidDataException>(()=>CharacterRosterStore.Load(directory),"lista danificada não é substituída");
            Check(File.ReadAllText(rosterPath)=="{ invalid","arquivo danificado preservado para recuperação");
            File.WriteAllText(rosterPath,"{\"version\":1,\"entries\":[{\"profile\":\"../outro\",\"displayName\":\"Invasor\"}]}");
            Reject<InvalidDataException>(()=>CharacterRosterStore.Load(directory),"travessia de caminho recusada");
            Debug.Log("[MENU ROSTER PASS] "+assertions+" verificações: importação, múltiplos personagens, identidade preservada, origens e corrupção.");
        }
        finally { if(Directory.Exists(directory))Directory.Delete(directory,true); }
    }
    private static void CheckSecondLauncher(string directory)
    {
        string fresh = Path.Combine(directory,"fresh-second-launcher");
        CharacterRoster roster = CharacterRosterStore.Load(fresh);
        CharacterRosterEntry entry = CharacterRosterStore.EnsureSecondLauncherCharacter(fresh,roster,"  Guerreiro 2  ");
        Check(entry.profile=="segundo"&&entry.displayName=="Guerreiro 2","segundo atalho cria e seleciona card aprovado sem credencial anterior");
        Check(CharacterRosterStore.Load(fresh).entries.Length==2,"card do segundo atalho é persistido");
        Check(!Directory.Exists(Path.Combine(fresh,"Online","segundo")),"card do segundo atalho não cria credencial nem pasta de identidade");
        string path=Path.Combine(fresh,"Online","characters.json"),saved=File.ReadAllText(path);
        CharacterRosterEntry repeated=CharacterRosterStore.EnsureSecondLauncherCharacter(fresh,roster,"Outro nome");
        Check(repeated.profile=="segundo"&&repeated.displayName=="Guerreiro 2"&&roster.entries.Length==2,"reabrir segundo atalho não duplica nem renomeia personagem");
        Check(File.ReadAllText(path)==saved,"reabertura do segundo atalho preserva lista byte a byte");
        string full=Path.Combine(directory,"full-second-launcher");
        CharacterRoster crowded=CharacterRosterStore.Load(full);
        while(crowded.entries.Length<CharacterRosterStore.MaximumCharacters)
            CharacterRosterStore.AddCharacter(full,crowded,"Guerreiro "+crowded.entries.Length);
        string fullPath=Path.Combine(full,"Online","characters.json"),original=File.ReadAllText(fullPath);
        Reject<InvalidOperationException>(()=>CharacterRosterStore.EnsureSecondLauncherCharacter(full,crowded,"Guerreiro 2"),"segundo atalho respeita limite de oito personagens");
        Check(File.ReadAllText(fullPath)==original&&crowded.entries.Length==8&&!Directory.Exists(Path.Combine(full,"Online","segundo")),"segundo atalho lotado preserva lista e não cria identidade");
    }
    private static void CheckPendingNicknames(string directory)
    {
        string root=Path.Combine(directory,"pending-nickname");
        CharacterRoster roster=CharacterRosterStore.Load(root);
        OnlineIdentityStore.LoadOrCreate(root,"principal");
        string identityPath=OnlineIdentityStore.GetIdentityPath(root,"principal"),identity=File.ReadAllText(identityPath);
        CharacterRosterEntry updated=CharacterRosterStore.UpdatePendingNickname(root,roster,"principal","  Guardião   Azul  ");
        Check(updated.profile=="principal"&&updated.displayName=="Guardião Azul","alteração pendente normaliza espaços sem trocar perfil");
        Check(CharacterRosterStore.Load(root).entries[0].displayName=="Guardião Azul","nick pendente persiste apenas rótulo local");
        Check(File.ReadAllText(identityPath)==identity,"trocar nick recusado preserva credencial byte a byte");
        Check(CharacterNickname.Key("Guerreiro")==CharacterNickname.Key("ＧＵＥＲＲＥＩＲＯ"),"chave de nick normaliza caixa e Unicode equivalente");
        Reject<ArgumentException>(()=>CharacterRosterStore.UpdatePendingNickname(root,roster,"principal",""),"nick vazio recusado");
        Reject<ArgumentException>(()=>CharacterRosterStore.UpdatePendingNickname(root,roster,"principal","Herói!"),"pontuação não permitida recusada");
        Reject<ArgumentException>(()=>CharacterRosterStore.UpdatePendingNickname(root,roster,"ausente","Outro Herói"),"perfil ausente não é criado na alteração de nick");
        roster.entries[0].displayName="Nome antigo!";CharacterRosterStore.Save(root,roster);
        Check(CharacterRosterStore.Load(root).entries[0].displayName=="Nome antigo!","rótulos legados continuam carregando sem forçar renomeação");
    }
    private static void Check(bool value,string text)
    { assertions++; if(!value)throw new BuildFailedException("Lista de personagens: "+text); }
    private static void Reject<T>(Action action,string text) where T:Exception
    { try{action();}catch(T){assertions++;return;}throw new BuildFailedException("Lista de personagens deveria recusar: "+text); }
}
