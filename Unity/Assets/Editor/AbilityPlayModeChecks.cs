using System;
using System.Reflection;
using GuerraDasNacoes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class AbilityPlayModeChecks
{
    private const string Key = "GuerraDasNacoes.AbilityChecks.";
    private static PrototypeCombat combat;
    private static PrototypeCharacter character;
    private static PrototypeMonster monster;
    private static PrototypeAbilities abilities;
    private static GameObject wall;
    private static float nextStageAt;
    private static float manaAtRegenerationStart;
    private static float regenerationStartedAt;
    private static PlayerProgression expectedProgression;

    static AbilityPlayModeChecks()
    {
        EditorApplication.update += Tick;
    }

    // CLI: -executeMethod AbilityPlayModeChecks.Run, sem -quit.
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Inicie a validação fora do PlayMode.");
        nextStageAt = 0f;
        SessionState.SetBool(Key + "Active", false);
        EditorSceneManager.OpenScene("Assets/Scenes/Patio.unity", OpenSceneMode.Single);
        SessionState.SetBool(Key + "Entered", false);
        SessionState.SetInt(Key + "Stage", 0);
        SessionState.SetFloat(Key + "Timeout", (float)EditorApplication.timeSinceStartup + 40f);
        SessionState.SetBool(Key + "Active", true);
        EditorApplication.EnterPlaymode();
    }

    private static void Tick()
    {
        if (!SessionState.GetBool(Key + "Active", false))
            return;
        try
        {
            Require(EditorApplication.timeSinceStartup < SessionState.GetFloat(Key + "Timeout", 0f),
                "validação excedeu 40 segundos");
            if (!EditorApplication.isPlaying)
            {
                Require(!SessionState.GetBool(Key + "Entered", false), "PlayMode terminou antes da validação");
                return;
            }
            SessionState.SetBool(Key + "Entered", true);
            if (EditorApplication.isCompiling || Time.frameCount < 3 || Time.time < nextStageAt)
                return;

            switch (SessionState.GetInt(Key + "Stage", 0))
            {
                case 0: CheckInvalidUsesAndHeavy(); break;
                case 1: CheckGuard(); break;
                case 2: CheckHealing(); break;
                case 3: CheckRegeneration(); break;
                case 4: CheckGuardExpiryAndInsufficientMana(); break;
                case 5: CheckDeathResetAndLevelHealing(); break;
                case 6: CheckSharedCooldownAndExperience(); break;
                case 7: CheckFinalReward(); break;
                default: throw new InvalidOperationException("Etapa inválida.");
            }
        }
        catch (Exception error)
        {
            Finish(false, error.ToString());
        }
    }

    private static void CheckInvalidUsesAndHeavy()
    {
        combat = UnityEngine.Object.FindFirstObjectByType<PrototypeCombat>();
        character = UnityEngine.Object.FindFirstObjectByType<PrototypeCharacter>();
        Require(combat != null && character != null, "componentes de combate ausentes");
        monster = combat.Monster;
        Require(monster != null && monster.MaxHealth >= 240, "guardião com perfil ampliado deve existir");
        expectedProgression = new PlayerProgression();
        abilities = combat.GetComponent<PrototypeAbilities>();
        Require(abilities != null, "PrototypeAbilities deve existir no personagem");
        Time.timeScale = 1f;
        // Isolate power rules; retaliation itself is exercised by CombatPlayModeChecks.
        monster.enabled = false;
        foreach (PrototypeMonster enemy in combat.Monsters)
            if (enemy != monster) enemy.gameObject.SetActive(false);
        character.ResetPosition();
        Equal(100, combat.Stats.Health, "vida inicial");
        Near(100f, abilities.Mana, "mana inicial");
        Equal(8, PrototypeAbilities.SlotCount, "oito espaços na barra");
        for (int slot = 0; slot < PrototypeAbilities.SlotCount; slot++)
        {
            Require(abilities.IsAssigned(slot) == (slot < 5), "cinco espaços atribuídos");
            if (slot >= 5)
            {
                Require(abilities.GetName(slot) == string.Empty && !abilities.CanUse(slot) && !abilities.TryUse(slot),
                    "espaços vazios não executam ações");
                Near(0f, abilities.GetCooldownRemaining(slot), "espaço vazio sem recarga");
            }
        }
        Require(!abilities.TryUse(-1) && !abilities.TryUse(8), "índices inválidos são recusados");
        Require(!abilities.CanUse(3) && !abilities.TryUse(3), "cura com vida cheia recusada");
        Near(100f, abilities.Mana, "cura inválida não consome mana");
        Near(0f, abilities.GetCooldownRemaining(3), "cura inválida não inicia recarga");

        // Free casts always spend their normal resources, even when the area is empty.
        combat.SetTarget(null);
        MovePlayer(4.5f);
        Require(abilities.CanUse(1) && abilities.TryUse(1), "pesado sem alvo pode ser lançado no vazio");
        Near(84f, abilities.Mana, "golpe vazio custa16MP");
        Require(abilities.GetCooldownRemaining(1) > 4.9f, "golpe vazio inicia recarga");
        character.ResetPosition();
        MovePlayer(1.4f);
        Require(abilities.TryUse(1), "Corte Pesado válido deve funcionar");
        Equal(monster.MaxHealth, monster.CurrentHealth, "dano pesado aguarda o frame de impacto");
        Near(84f, abilities.Mana, "Corte Pesado custa 16 MP");
        Require(abilities.GetCooldownRemaining(1) > 4.9f, "Corte Pesado inicia cinco segundos de recarga");
        Require(!abilities.TryUse(1) && !combat.TryAttack() && !abilities.TryUse(2),
            "recargas impedem múltiplas ações no mesmo frame");
        Near(84f, abilities.Mana, "ações bloqueadas não consomem mana");
        Equal(monster.MaxHealth, monster.CurrentHealth, "ações bloqueadas não antecipam dano");
        Advance(1, 0.25f);
    }

    private static void CheckGuard()
    {
        Equal(monster.MaxHealth - 32, monster.CurrentHealth, "pesado aplica32 no impacto sem derrotar guardião resistente");
        float before = abilities.Mana;
        Require(abilities.TryUse(2), "Guarda de Ferro deve funcionar após intervalo global");
        Near(before - 20f, abilities.Mana, "Guarda custa 20 MP");
        Require(abilities.IsGuarding && abilities.GuardRemaining > 3.9f, "Guarda dura quatro segundos");
        Require(abilities.GetCooldownRemaining(2) > 11.9f, "Guarda inicia recarga de doze segundos");
        Equal(4, combat.ReceiveDamage(8), "Guarda reduz oito de dano para quatro");
        Equal(4, combat.ReceiveDamage(7), "Guarda arredonda metade ímpar para cima");
        Equal(0, combat.ReceiveDamage(int.MinValue), "dano negativo ignorado durante Guarda");
        Equal(92, combat.Stats.Health, "vida após dano mitigado");
        Require(!abilities.TryUse(3), "intervalo global impede cura no mesmo frame da Guarda");
        Near(before - 20f, abilities.Mana, "cura bloqueada não consome mana");
        Equal(42, combat.ReceiveDamage(84), "mitigação também funciona com dano maior");
        Equal(50, combat.Stats.Health, "fixture de cura parcial");
        Advance(2, 0.25f);
    }

    private static void CheckHealing()
    {
        float before = abilities.Mana;
        Require(abilities.TryUse(3), "Segundo Fôlego cura personagem vivo ferido");
        Equal(85, combat.Stats.Health, "Segundo Fôlego recupera 35% de 100 HP");
        Near(before - 25f, abilities.Mana, "Segundo Fôlego custa 25 MP");
        Require(abilities.GetCooldownRemaining(3) > 14.9f, "cura inicia quinze segundos de recarga");
        Require(!abilities.TryUse(3), "cura não pode ser repetida imediatamente");
        manaAtRegenerationStart = abilities.Mana;
        regenerationStartedAt = Time.time;
        Advance(3, 0.8f);
    }

    private static void CheckRegeneration()
    {
        float expected = Mathf.Min(abilities.MaxMana, manaAtRegenerationStart + (Time.time - regenerationStartedAt) * 8f);
        Require(Mathf.Abs(abilities.Mana - expected) <= Mathf.Max(0.15f, Time.deltaTime * 8f + 0.1f),
            "mana regenera oito pontos por segundo enquanto vivo");
        Require(abilities.Mana > manaAtRegenerationStart + 3f, "regeneração aumenta mana");
        Require(!abilities.CanUse(3) && !abilities.TryUse(3), "recarga individual impede nova cura com vida incompleta");
        Advance(4, 0f);
    }

    private static void CheckGuardExpiryAndInsufficientMana()
    {
        if (abilities.IsGuarding)
            return;
        Near(0f, abilities.GuardRemaining, "Guarda termina após quatro segundos");
        Require(abilities.GetCooldownRemaining(2) > 7f, "fim da Guarda não encerra sua recarga de doze segundos");
        Equal(8, combat.ReceiveDamage(8), "dano normal retorna após a Guarda");
        combat.GrantExperience(60);
        expectedProgression.GrantExperience(60);
        character.ResetPosition();
        Equal(60, combat.Stats.Experience, "reinício preserva experiência");
        AssertReset(100);
        MovePlayer(1.4f);
        combat.SetTarget(monster);
        combat.ReceiveDamage(60);
        FieldInfo manaField = typeof(PrototypeAbilities).GetField("mana", BindingFlags.Instance | BindingFlags.NonPublic);
        Require(manaField != null, "fixture de pouca mana disponível");
        manaField.SetValue(abilities, 15f);
        for (int slot = 1; slot < 5; slot++)
        {
            Require(!abilities.CanUse(slot) && !abilities.TryUse(slot), "mana insuficiente recusa poder " + slot);
            Near(0f, abilities.GetCooldownRemaining(slot), "mana insuficiente não inicia recarga");
        }
        Near(15f, abilities.Mana, "poderes recusados preservam mana");
        combat.ReceiveDamage(int.MaxValue);
        Require(!combat.IsAlive && !abilities.IsGuarding, "personagem morto sem Guarda ativa");
        for (int slot = 0; slot < 5; slot++)
            Require(!abilities.CanUse(slot) && !abilities.TryUse(slot), "personagem morto não usa poderes");
        Require(!combat.TryAttack(), "API de ataque também recusa personagem morto");
        Advance(5, 0.4f);
    }

    private static void CheckDeathResetAndLevelHealing()
    {
        Near(15f, abilities.Mana, "mana não regenera durante morte");
        character.ResetPosition();
        AssertReset(100);
        Equal(60, combat.Stats.Experience, "morte e reinício preservam experiência");
        Require(abilities.TryUse(2), "Guarda pode ser reativada após reinício");
        combat.ReceiveDamage(int.MaxValue);
        Require(!combat.IsAlive && !abilities.IsGuarding && abilities.GuardRemaining == 0f,
            "morte encerra Guarda ativa imediatamente");
        character.ResetPosition();
        AssertReset(100);
        MovePlayer(1.4f);
        monster.TakeDamage(int.MaxValue, combat);
        expectedProgression.GrantExperience(monster.ExperienceReward);
        Equal(expectedProgression.Level, combat.Stats.Level, "recompensa individual continua elevando nível");
        Equal(expectedProgression.Experience, combat.Stats.Experience, "experiência excedente preservada");
        Equal(expectedProgression.AttackDamage, combat.Stats.AttackDamage, "evolução atualiza dano básico");
        combat.ReceiveDamage(80);
        Require(abilities.TryUse(3), "cura funciona após evolução");
        int expectedHeal = expectedProgression.MaxHealth * 35 / 100;
        Equal(expectedProgression.MaxHealth - 80 + expectedHeal, combat.Stats.Health,
            "cura usa 35% da nova vida máxima após evolução");
        character.ResetPosition();
        AssertReset(expectedProgression.MaxHealth);
        Equal(expectedProgression.Level, combat.Stats.Level, "reinício preserva nível evoluído");
        Equal(expectedProgression.Experience, combat.Stats.Experience, "reinício preserva experiência após evolução");
        MovePlayer(1.4f);
        combat.SetTarget(monster);
        Require(abilities.TryUse(1), "Corte Pesado funciona após evolução e reinício");
        Equal(monster.MaxHealth, monster.CurrentHealth, "pesado após evolução também aguarda impacto");
        Equal(expectedProgression.AttackDamage, combat.Stats.AttackDamage, "Corte Pesado não altera dano básico permanente");
        Advance(6, 0.7f);
    }

    private static void CheckSharedCooldownAndExperience()
    {
        int expectedHeavyDamage = (expectedProgression.AttackDamage * 18 + 5) / 10;
        Equal(monster.MaxHealth - expectedHeavyDamage, monster.CurrentHealth, "pesado usa dano aumentado após evolução");
        Require(monster.IsAlive, "guardião resistente sobrevive ao pesado isolado");
        monster.TakeDamage(monster.CurrentHealth - expectedProgression.AttackDamage, combat);
        Equal(expectedProgression.AttackDamage, monster.CurrentHealth, "fixture do golpe básico fatal");
        Require(abilities.GetCooldownRemaining(1) > 4f, "recarga individual do pesado continua ativa");
        Require(abilities.CanUse(0) && abilities.TryUse(0), "básico volta após 0,65 segundo sem esperar recarga do pesado");
        Advance(7, .28f);
    }

    private static void CheckFinalReward()
    {
        Require(!monster.IsAlive, "básico mantém seu dano e finaliza monstro enfraquecido");
        expectedProgression.GrantExperience(monster.ExperienceReward);
        Equal(expectedProgression.Level, combat.Stats.Level, "recompensa por monstro respeita perfil e progressão");
        Equal(expectedProgression.Experience, combat.Stats.Experience, "recompensa por monstro preserva excedente de XP");
        monster.TakeDamage(int.MaxValue, combat);
        Equal(expectedProgression.Experience, combat.Stats.Experience, "poderes não duplicam recompensa de morto");
        Finish(true, "Mana, recargas, cinco poderes, rejeições, Guarda, cura, morte, reinício e experiência validados.");
    }

    private static void AssertReset(int expectedHealth)
    {
        Equal(expectedHealth, combat.Stats.Health, "reinício restaura toda a vida");
        Near(100f, abilities.Mana, "reinício restaura mana");
        Require(!abilities.IsGuarding && !abilities.IsDamageBoosted, "reinício limpa efeitos temporários");
        Near(0f, abilities.GlobalCooldownRemaining, "reinício limpa intervalo global");
        for (int slot = 0; slot < 5; slot++)
            Near(0f, abilities.GetCooldownRemaining(slot), "reinício limpa recarga do espaço " + slot);
    }

    private static void MovePlayer(float horizontalDistance)
    {
        CharacterController controller = character.GetComponent<CharacterController>();
        Vector3 destination = monster.transform.position + Vector3.right * horizontalDistance;
        destination.y = character.transform.position.y;
        controller.enabled = false;
        character.transform.position = destination;
        Vector3 direction = monster.transform.position - destination;
        direction.y = 0f;
        character.transform.rotation = Quaternion.LookRotation(direction);
        controller.enabled = true;
        Physics.SyncTransforms();
    }

    private static void Advance(int stage, float wait)
    {
        SessionState.SetInt(Key + "Stage", stage);
        nextStageAt = Time.time + wait;
    }

    private static void Equal(int expected, int actual, string message)
    {
        Require(expected == actual, message + $": esperado {expected}, recebido {actual}");
    }

    private static void Near(float expected, float actual, string message)
    {
        Require(Mathf.Abs(expected - actual) < 0.01f, message + $": esperado {expected}, recebido {actual}");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void Finish(bool success, string message)
    {
        SessionState.SetBool(Key + "Active", false);
        if (wall != null)
            UnityEngine.Object.Destroy(wall);
        if (success)
            Debug.Log("[ABILITY PLAY PASS] " + message);
        else
            Debug.LogError("[ABILITY PLAY FAIL] " + message);
        EditorApplication.Exit(success ? 0 : 1);
    }
}
