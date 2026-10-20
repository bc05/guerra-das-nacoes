using System;
using System.Linq;
using GuerraDasNacoes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class AttributeAbilityPlayModeChecks
{
    private const string Key = "GuerraDasNacoes.AttributeChecks.";
    private static PrototypeCombat combat;
    private static PrototypeCharacter character;
    private static PrototypeAbilities abilities;
    private static PrototypeStatusEffects effects;
    private static PrototypeMonster enemy;
    private static float nextAt;
    private static int renderers;
    private static int[] meshIds;

    static AttributeAbilityPlayModeChecks() { EditorApplication.update += Tick; }

    [MenuItem("Guerra das Nações/Validar atributos, Fúria e efeitos")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Inicie a validação fora do PlayMode.");
        nextAt = 0f;
        SessionState.SetBool(Key + "Active", false);
        EditorSceneManager.OpenScene("Assets/Scenes/Patio.unity", OpenSceneMode.Single);
        SessionState.SetBool(Key + "Entered", false);
        SessionState.SetInt(Key + "Stage", 0);
        SessionState.SetFloat(Key + "Timeout", (float)EditorApplication.timeSinceStartup + 55f);
        SessionState.SetBool(Key + "Active", true);
        EditorApplication.EnterPlaymode();
    }

    private static void Tick()
    {
        if (!SessionState.GetBool(Key + "Active", false)) return;
        try
        {
            Require(EditorApplication.timeSinceStartup < SessionState.GetFloat(Key + "Timeout", 0), "timeout");
            if (!EditorApplication.isPlaying)
            {
                Require(!SessionState.GetBool(Key + "Entered", false), "PlayMode terminou antecipadamente");
                return;
            }
            SessionState.SetBool(Key + "Entered", true);
            if (EditorApplication.isCompiling || Time.frameCount < 3 || Time.time < nextAt) return;
            switch (SessionState.GetInt(Key + "Stage", 0))
            {
                case 0: Initialize(); break;
                case 1:
                    Require(effects.DamageBoostVisible, "Fúria possui efeito visível enquanto ativa");
                    Require(combat.TryAttack(), "ataque básico com Fúria");
                    combat.GrantExperience(100);
                    Require(combat.Stats.Level == 2 && combat.Stats.UnspentPoints == 5, "level concede cinco pontos");
                    Require(combat.Stats.TryAllocate(HeroAttribute.Intelligence, 5), "novos pontos podem ser distribuídos");
                    Near(8.08f, abilities.DamageBoostDurationSeconds, "duração da Fúria fixada na ativação");
                    Advance(2, .25f); break;
                case 2:
                    Require(enemy.CurrentHealth == enemy.MaxHealth - 23, "básico usa dano23 capturado antes da evolução, sem dupla arredondagem");
                    Require(abilities.TryUse(2), "Guarda funciona junto com Fúria");
                    Near(4.14f, abilities.GuardDurationSeconds, "Guarda usa a inteligência atual");
                    Require(combat.ReceiveDamage(7) == 3, "defesa e Guarda arredondam somente após compor multiplicadores");
                    Advance(3, .25f); break;
                case 3:
                    Require(effects.GuardVisible && effects.DamageBoostVisible, "os dois efeitos coexistem");
                    Require(abilities.TryUse(3), "cura do herói ferido");
                    Advance(4, .15f); break;
                case 4:
                    Require(effects.HealingVisible && effects.GuardVisible && effects.DamageBoostVisible, "cura verde não cancela efeitos persistentes");
                    character.ResetPosition();
                    AssertReset();
                    Require(combat.Stats.AttackPoints == 2 && combat.Stats.DefensePoints == 1 && combat.Stats.IntelligencePoints == 7 &&
                        combat.Stats.UnspentPoints == 0, "reinício preserva distribuição e pontos");
                    Require(abilities.TryUse(4), "Fúria volta após reinício");
                    Advance(5, .25f); break;
                case 5:
                    Require(abilities.TryUse(2), "Guarda antes da derrota");
                    Advance(6, .15f); break;
                case 6:
                    Require(effects.GuardVisible && effects.DamageBoostVisible, "efeitos presentes antes da derrota");
                    combat.ReceiveDamage(int.MaxValue);
                    Require(!combat.IsAlive && !abilities.IsGuarding && !abilities.IsDamageBoosted, "derrota cancela os bônus");
                    Require(!effects.GuardVisible && !effects.DamageBoostVisible && !effects.HealingVisible, "derrota remove todos os efeitos");
                    Require(!abilities.TryUse(4), "Fúria recusada durante derrota");
                    character.ResetPosition();
                    AssertReset();
                    Require(abilities.TryUse(4), "fixture de expiração");
                    Near(8.28f, abilities.DamageBoostDurationSeconds, "nova ativação usa inteligência7");
                    Advance(7, .25f); break;
                case 7:
                    Require(abilities.TryUse(2), "Guarda simultânea na fixture de expiração");
                    Advance(8, 4.3f); break;
                case 8:
                    Require(!abilities.IsGuarding && !effects.GuardVisible, "Guarda e efeito terminam juntos");
                    Require(abilities.IsDamageBoosted && effects.DamageBoostVisible, "Fúria mais longa continua visível");
                    Advance(9, 4f); break;
                case 9:
                    Require(!abilities.IsDamageBoosted && !effects.DamageBoostVisible, "Fúria e efeito terminam juntos");
                    Require(abilities.GetCooldownRemaining(4) > 10f, "fim do bônus não encerra recarga");
                    Require(!effects.HealingVisible, "cura não deixa efeito permanente");
                    Require(combat.Stats.GetBasicDamage(abilities.OutgoingDamageMultiplier) == 23, "expiração restaura dano sem bônus");
                    character.ResetPosition();
                    Require(abilities.TryUse(4), "fixture de desativação");
                    Advance(10, .25f); break;
                case 10:
                    Require(effects.DamageBoostVisible, "Fúria visível antes da desativação");
                    combat.enabled = false;
                    Require(!abilities.IsDamageBoosted && !effects.DamageBoostVisible, "desativar combate limpa bônus e efeito");
                    Require(!combat.CanAttack && !combat.TryAttack() && !abilities.CanUse(2) && !abilities.TryUse(2),
                        "combate desativado não aceita novos ataques ou poderes");
                    combat.enabled = true;
                    character.ResetPosition();
                    Require(abilities.TryUse(4), "bônus não fica bloqueado após reativação");
                    Advance(11, .25f); break;
                case 11:
                    abilities.enabled = false;
                    Require(!abilities.IsDamageBoosted && !effects.DamageBoostVisible, "desativar habilidades cancela efeitos");
                    abilities.enabled = true;
                    character.ResetPosition();
                    AssertReset();
                    Require(renderers == effects.GetComponentsInChildren<Renderer>(true).Length, "usos reutilizam renderers");
                    int[] finalIds = effects.GetComponentsInChildren<MeshFilter>(true).Select(f => f.sharedMesh.GetInstanceID()).OrderBy(id => id).ToArray();
                    Require(meshIds.SequenceEqual(finalIds), "usos reutilizam as malhas");
                    Finish(true, "Atributos reais, Fúria, dano capturado, inteligência/duração, Guarda/defesa, cura, expiração, derrota, reinício, desativação e reutilização de VFX."); break;
            }
        }
        catch (Exception error) { Finish(false, error.ToString()); }
    }

    private static void Initialize()
    {
        combat = UnityEngine.Object.FindFirstObjectByType<PrototypeCombat>();
        Require(combat != null, "personagem presente");
        character = combat.GetComponent<PrototypeCharacter>();
        abilities = combat.GetComponent<PrototypeAbilities>();
        effects = combat.GetComponent<PrototypeStatusEffects>();
        Require(effects != null && abilities.IsAssigned(4), "quinto poder e componente visual configurados");
        character.ResetPosition();
        character.enabled = false;
        foreach (PrototypeMonster monster in combat.Monsters) monster.enabled = false;
        enemy = combat.Monster;
        foreach (PrototypeMonster monster in combat.Monsters) if (monster != enemy) monster.gameObject.SetActive(false);
        CharacterController collider = enemy.GetComponent<CharacterController>();
        collider.enabled = false;
        enemy.transform.position = new Vector3(0, .06f, 1.9f);
        collider.enabled = true;
        Physics.SyncTransforms();
        Require(combat.Stats.UnspentPoints == 5 && combat.Stats.TryAllocate(HeroAttribute.Attack, 2) &&
            combat.Stats.TryAllocate(HeroAttribute.Defense) && combat.Stats.TryAllocate(HeroAttribute.Intelligence, 2), "cinco pontos iniciais distribuídos");
        Require(combat.Stats.GetBasicDamage() == 19 && combat.Stats.GetSkillDamage() == 34, "multiplicadores permanentes");
        renderers = effects.GetComponentsInChildren<Renderer>(true).Length;
        meshIds = effects.GetComponentsInChildren<MeshFilter>(true).Select(f => f.sharedMesh.GetInstanceID()).OrderBy(id => id).ToArray();
        Require(renderers >= 3 && meshIds.Length >= 3, "efeitos possuem geometria renderizável");
        Require(abilities.TryUse(4), "Fúria pode ser ativada");
        Near(75f, abilities.Mana, "Fúria custa25MP");
        Require(abilities.IsDamageBoosted && abilities.GetCooldownRemaining(4) > 19.9f, "bônus e recarga começam");
        Require(combat.Stats.GetBasicDamage(abilities.OutgoingDamageMultiplier) == 23 &&
            combat.Stats.GetSkillDamage(1.8d, true, abilities.OutgoingDamageMultiplier) == 43, "Fúria combina multiplicadores sem arredondamento intermediário");
        Require(!abilities.TryUse(4) && !abilities.TryUse(2), "efeito ativo e intervalo global impedem empilhar ações");
        Near(75f, abilities.Mana, "usos recusados não gastam mana");
        Advance(1, .25f);
    }

    private static void AssertReset()
    {
        Require(combat.IsAlive && combat.Stats.Health == combat.Stats.MaxHealth, "vida restaurada");
        Require(!abilities.IsGuarding && !abilities.IsDamageBoosted, "bônus limpos");
        Require(!effects.GuardVisible && !effects.DamageBoostVisible && !effects.HealingVisible, "efeitos limpos");
        Near(100f, abilities.Mana, "mana restaurada");
        for (int slot = 0; slot < 5; slot++) Near(0f, abilities.GetCooldownRemaining(slot), "recarga limpa");
    }

    private static void Advance(int stage, float seconds)
    {
        SessionState.SetInt(Key + "Stage", stage);
        nextAt = Time.time + seconds;
    }
    private static void Near(float expected, float actual, string message) { Require(Mathf.Abs(expected - actual) < .05f, message + ": " + actual); }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Finish(bool passed, string message)
    {
        SessionState.SetBool(Key + "Active", false);
        Time.timeScale = 1f;
        if (passed) Debug.Log("[ATRIBUTOS PLAY PASS] " + message);
        else Debug.LogError("[ATRIBUTOS PLAY FAIL] " + message);
        if (Application.isBatchMode) EditorApplication.Exit(passed ? 0 : 1);
        else EditorApplication.ExitPlaymode();
    }
}
