using System;
using System.Linq;
using GuerraDasNacoes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Checks visual snapshot idempotency, reachable boss dimensions and city isolation.</summary>
[InitializeOnLoad]
public static class WorldExpansionPlayModeChecks
{
    private const string Key="GuerraDasNacoes.FrontierChecks.";
    private static PrototypeMonster fixture;
    private static PrototypeCombat combat;
    private static OnlineMonsterState state;
    private static GameObject frontier;
    private static int stage;
    private static float nextAt;
    static WorldExpansionPlayModeChecks(){EditorApplication.update+=Tick;}
    public static void Run()
    {
        SessionState.SetBool(Key+"Active",false);
        EditorSceneManager.OpenScene("Assets/Scenes/Patio.unity",OpenSceneMode.Single);
        SessionState.SetBool(Key+"Entered",false);SessionState.SetFloat(Key+"Timeout",(float)EditorApplication.timeSinceStartup+60);
        SessionState.SetBool(Key+"Active",true);stage=0;nextAt=0;EditorApplication.EnterPlaymode();
    }
    private static void Tick()
    {
        if(!SessionState.GetBool(Key+"Active",false))return;
        try
        {
            Require(EditorApplication.timeSinceStartup<SessionState.GetFloat(Key+"Timeout",0),"tempo máximo excedido");
            if(!EditorApplication.isPlaying){Require(!SessionState.GetBool(Key+"Entered",false),"PlayMode encerrado antes de concluir");return;}
            SessionState.SetBool(Key+"Entered",true);
            if(EditorApplication.isCompiling||Time.frameCount<4||Time.time<nextAt)return;
            if(stage==0)
            {
                combat=Object.FindFirstObjectByType<PrototypeCombat>();
                Require(combat!=null&&combat.Monsters.Count==3,"encontro original de três trolls preservado");
                PrototypeOnlineClient client=combat.GetComponent<PrototypeOnlineClient>();
                Require(client!=null&&!client.IsOnlineMode,"teste usa progresso descartável offline");
                frontier=GameObject.Find(PrototypeWorldExpansion.RootName);
                Require(frontier!=null&&frontier.activeInHierarchy,"vale visível nas ruínas");
                foreach(PrototypeMonster enemy in combat.Monsters)enemy.enabled=false;
                GameObject clone=Object.Instantiate(combat.Monsters[0].gameObject);
                fixture=clone.GetComponent<PrototypeMonster>();fixture.enabled=false;
                state=new OnlineMonsterState{id="boss-check",name="Chefe de inspeção",mapId="ruins",level=25,
                    x=0,z=61,health=12000,maxHealth=12000,damage=70,experience=1000,isBoss=true,visualKind="boss-troll",visualScale=1.7f,
                    attackRange=2.5f,moveSpeed=2.5f,aggroRadius=12f,leashRadius=26f};
                fixture.ApplyOnlineState(state,0);PrototypeWorldExpansion.ConfigureMonsterVisual(fixture,state);
                MonsterVariantView view=fixture.GetComponent<MonsterVariantView>();
                Require(view!=null&&view.Kind=="boss-troll"&&view.Owned.Count>=15,"chefe possui armadura, coroa, estandarte e brasa");
                CharacterController body=fixture.GetComponent<CharacterController>();
                Require(body.radius*Mathf.Abs(fixture.transform.lossyScale.x)<=1.651f,"chefe pode ser alcançado em combate corpo a corpo");
                Require(fixture.transform.lossyScale.y>1.6f&&fixture.MaxHealth==12000&&fixture.AttackDamage==70,"silhueta ampliada respeita atributos autoritativos");
                Require(Mathf.Abs(fixture.AttackReach-2.5f)<.001f&&Mathf.Abs(fixture.AggroRadius-12f)<.001f,
                    "aviso de alcance e agressão seguem perfil autoritativo do chefão");
                int pieces=view.Owned.Count;GameObject first=view.Owned[0];Material material=first.GetComponent<Renderer>().sharedMaterial;
                for(int n=0;n<100;n++)PrototypeWorldExpansion.ConfigureMonsterVisual(fixture,state);
                Require(view.Owned.Count==pieces&&view.Owned[0]==first&&view.Owned[0].GetComponent<Renderer>().sharedMaterial==material,
                    "cem snapshots não duplicam peças nem materiais");
                state.isBoss=false;state.visualKind="orc";state.visualScale=.93f;state.id="orc-shaman-check";
                PrototypeWorldExpansion.ConfigureMonsterVisual(fixture,state);
                Require(view.Kind=="orc"&&view.Owned.Any(p=>p!=null&&p.name.Contains("Bastão")),"variante xamã tem bastão articulado");
                Require(!view.Owned.Any(p=>p!=null&&p.name.Contains("Coroa")),"troca de perfil remove coroa de chefe");
                state.visualKind="troll";state.visualScale=1;
                PrototypeWorldExpansion.ConfigureMonsterVisual(fixture,state);
                Require(view.Owned.Count==0&&Mathf.Abs(fixture.transform.localScale.x-view.BaseScale.x)<.001f,"retorno ao troll restaura escala e retira peças");
                state.visualScale=3;PrototypeWorldExpansion.ConfigureMonsterVisual(fixture,state);
                Require(Mathf.Abs(fixture.transform.localScale.x-view.BaseScale.x*3)<.001f,"escala máxima configurável não é reduzida silenciosamente");
                state.visualScale=.5f;PrototypeWorldExpansion.ConfigureMonsterVisual(fixture,state);
                Require(Mathf.Abs(fixture.transform.localScale.x-view.BaseScale.x*.5f)<.001f,"escala mínima configurável é respeitada");
                combat.GetComponent<PrototypeCity>().ApplyMap("city");
                Require(!frontier.activeSelf,"expansão e colisões ocultas na cidade");
                stage=1;nextAt=Time.time+.05f;
            }
            else
            {
                combat.GetComponent<PrototypeCity>().ApplyMap("ruins");
                Require(frontier.activeSelf,"retorno às ruínas reativa a fronteira");
                Finish(true,"Mapa persistido, três encontros originais, chefe alcançável, atributos autoritativos, variantes articuladas idempotentes e transição cidade/ruínas.");
            }
        }
        catch(Exception error){Finish(false,error.ToString());}
    }
    private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    private static void Finish(bool success,string detail)
    {
        SessionState.SetBool(Key+"Active",false);
        if(fixture!=null)Object.Destroy(fixture.gameObject);
        if(success)Debug.Log("[WORLD EXPANSION PLAYMODE PASS] "+detail);else Debug.LogError("[WORLD EXPANSION PLAYMODE FAIL] "+detail);
        EditorApplication.Exit(success?0:1);
    }
}
