using GuerraDasNacoes;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

public static class CombatRulesChecks
{
    [MenuItem("Guerra das Nações/Validar regras de combate")]
    public static void Run()
    {
        CheckDamageAndRestoration();
        CheckPartialHealing();
        CheckExperienceAndLevelUp();
        CheckMultipleLevels();
        CheckLevelCapAndOverflow();
        CheckAttributeAllocation();
        CheckAttributeScaling();
        CheckSingleDamageRounding();
        CheckScalingLimits();
        CheckEqualPointBuilds();
        CheckWeaponRequirements();
        Debug.Log("[COMBATE TEST PASS] Vida, experiência, atributos, multiplicadores, arredondamento e requisitos de armas validados.");
    }

    private static void CheckDamageAndRestoration()
    {
        PlayerProgression player = new PlayerProgression();
        Equal(1, player.Level, "nível inicial");
        Equal(5, player.UnspentPoints, "cinco pontos disponíveis na criação");
        Equal(100, player.Health, "vida inicial");
        Equal(100, player.MaxHealth, "vida máxima inicial");
        Equal(18, player.AttackDamage, "dano inicial");
        Equal(100, player.ExperienceToNextLevel, "experiência inicial necessária");
        Require(player.IsAlive && !player.IsMaxLevel, "estado inicial");
        Equal(0, player.ReceiveDamage(0), "dano zero ignorado");
        Equal(0, player.ReceiveDamage(int.MinValue), "dano negativo ignorado");
        Equal(100, player.Health, "dano inválido preserva vida");
        Equal(37, player.ReceiveDamage(37), "dano aplicado");
        Equal(63, player.Health, "vida após dano");
        Equal(63, player.ReceiveDamage(int.MaxValue), "dano limitado à vida restante");
        Equal(0, player.Health, "vida nunca negativa");
        Require(!player.IsAlive, "morte ao chegar a zero");
        Equal(0, player.ReceiveDamage(10), "personagem morto não recebe dano adicional");
        player.GrantExperience(60);
        player.RestoreHealth();
        Equal(100, player.Health, "restauração de vida");
        Equal(60, player.Experience, "restauração preserva experiência");
        Require(player.IsAlive, "restauração recupera personagem");
    }

    private static void CheckExperienceAndLevelUp()
    {
        PlayerProgression player = new PlayerProgression();
        Equal(0, player.GrantExperience(0), "experiência zero ignorada");
        Equal(0, player.GrantExperience(int.MinValue), "experiência negativa ignorada");
        Equal(0, player.Experience, "experiência não fica negativa");
        Equal(0, player.GrantExperience(60), "primeiro monstro sem evolução");
        Equal(60, player.Experience, "primeiro monstro concede experiência");
        player.ReceiveDamage(40);
        Equal(1, player.GrantExperience(60), "segundo monstro sobe um nível");
        Equal(2, player.Level, "nível após dois monstros");
        Equal(20, player.Experience, "experiência excedente preservada");
        Equal(150, player.ExperienceToNextLevel, "experiência necessária no nível dois");
        Equal(120, player.MaxHealth, "evolução aumenta vida máxima");
        Equal(120, player.Health, "evolução restaura vida");
        Equal(22, player.AttackDamage, "evolução aumenta dano");
        Equal(10, player.UnspentPoints, "evolução concede cinco pontos");
        Equal(0, player.GrantExperience(-60), "experiência negativa após evolução ignorada");
        Equal(20, player.Experience, "experiência existente preservada");
    }

    private static void CheckPartialHealing()
    {
        PlayerProgression player = new PlayerProgression();
        Equal(0, player.Heal(35), "vida cheia não recebe cura");
        player.ReceiveDamage(60);
        Equal(0, player.Heal(0), "cura zero ignorada");
        Equal(0, player.Heal(int.MinValue), "cura negativa ignorada");
        Equal(40, player.Health, "cura inválida preserva vida");
        Equal(35, player.Heal(35), "cura parcial efetiva");
        Equal(75, player.Health, "vida após cura parcial");
        Equal(25, player.Heal(int.MaxValue), "cura máxima limitada sem overflow");
        Equal(100, player.Health, "cura limitada à vida máxima");
        player.ReceiveDamage(int.MaxValue);
        Equal(0, player.Heal(int.MaxValue), "cura não ressuscita personagem morto");
        Equal(0, player.Health, "morte preservada após tentativa de cura");
    }

    private static void CheckMultipleLevels()
    {
        PlayerProgression player = new PlayerProgression();
        player.ReceiveDamage(99);
        Equal(3, player.GrantExperience(475), "uma recompensa permite múltiplas evoluções");
        Equal(4, player.Level, "nível após múltiplas evoluções");
        Equal(25, player.Experience, "excedente após múltiplas evoluções");
        Equal(250, player.ExperienceToNextLevel, "experiência necessária no nível quatro");
        Equal(160, player.Health, "vida restaurada no último nível obtido");
        Equal(30, player.AttackDamage, "dano após múltiplas evoluções");
        Equal(20, player.UnspentPoints, "múltiplas evoluções concedem cinco pontos por nível");
        Equal(1, player.GrantExperience(225), "recompensa completa experiência restante");
        Equal(5, player.Level, "evolução no limite exato");
        Equal(0, player.Experience, "limite exato sem excedente");
    }

    private static void CheckLevelCapAndOverflow()
    {
        PlayerProgression player = new PlayerProgression();
        player.GrantExperience(99);
        Equal(49, player.GrantExperience(int.MaxValue), "recompensa máxima sem estouro");
        Equal(50, player.Level, "nível limitado a cinquenta");
        Require(player.IsMaxLevel, "limite de nível sinalizado");
        Equal(0, player.Experience, "experiência descartada no limite");
        Equal(0, player.ExperienceToNextLevel, "limite sem próximo nível");
        Equal(1080, player.MaxHealth, "vida máxima no limite");
        Equal(1080, player.Health, "vida restaurada no limite");
        Equal(214, player.AttackDamage, "dano no limite");
        Equal(250, player.UnspentPoints, "limite conserva todos os pontos de cinquenta níveis");
        player.ReceiveDamage(10);
        Equal(0, player.GrantExperience(int.MaxValue), "recompensa no limite ignorada");
        Equal(1070, player.Health, "recompensa sem evolução não restaura vida");
        Equal(0, player.Experience, "recompensas repetidas mantêm experiência zero");
        Equal(0, player.GrantExperience(int.MinValue), "recompensa negativa no limite ignorada");
        Equal(50, player.Level, "limite permanece estável");
        Equal(250, player.UnspentPoints, "recompensas no limite não geram pontos");
    }

    private static void CheckAttributeAllocation()
    {
        PlayerProgression player = new PlayerProgression();
        Require(!player.TryAllocate(HeroAttribute.Attack, 0), "zero pontos recusados");
        Require(!player.TryAllocate(HeroAttribute.Attack, int.MinValue), "pontos negativos recusados");
        Require(!player.TryAllocate(HeroAttribute.Defense, int.MaxValue), "alocação acima do saldo recusada");
        Require(!player.TryAllocate((HeroAttribute)99), "atributo inexistente recusado");
        Equal(5, player.UnspentPoints, "alocações inválidas conservam saldo");
        Equal(0, player.AttackPoints + player.DefensePoints + player.IntelligencePoints, "alocações inválidas conservam atributos");
        Require(player.TryAllocate(HeroAttribute.Attack, 2), "aloca dois pontos de ataque");
        Require(player.TryAllocate(HeroAttribute.Defense), "aloca um ponto de defesa");
        Require(player.TryAllocate(HeroAttribute.Intelligence, 2), "aloca dois pontos de inteligência");
        Equal(0, player.UnspentPoints, "saldo consumido exatamente");
        Require(!player.TryAllocate(HeroAttribute.Attack), "saldo vazio impede alocação");
        player.ReceiveDamage(int.MaxValue);
        player.GrantExperience(100);
        player.ReceiveDamage(int.MaxValue);
        Require(player.TryAllocate(HeroAttribute.Intelligence), "alocação de atributos permitida durante derrota");
        Equal(0, player.Health, "alocação não cura nem ressuscita");
        player.RestoreHealth();
        Equal(2, player.AttackPoints, "restauração preserva ataque");
        Equal(1, player.DefensePoints, "restauração preserva defesa");
        Equal(3, player.IntelligencePoints, "restauração preserva inteligência");
        Equal(10, player.AttackPoints + player.DefensePoints + player.IntelligencePoints + player.UnspentPoints,
            "pontos gastos e livres sempre correspondem ao nível");
    }

    private static void CheckAttributeScaling()
    {
        PlayerProgression player = new PlayerProgression();
        Equal(18, player.BaseAttackDamage, "base não muda com atributos");
        Equal(32, player.GetSkillDamage(), "habilidade legada sem atributos");
        Require(player.TryAllocate(HeroAttribute.Attack, 5), "build de ataque inicial");
        Near(1.1d, player.PhysicalAttackMultiplier, "ataque soma dois por cento por ponto");
        Near(1.05d, player.SkillAttackMultiplier, "habilidade soma um por cento de ataque por ponto");
        Equal(20, player.AttackDamage, "ataque aplica multiplicador físico");
        Equal(34, player.GetSkillDamage(), "habilidade não repete multiplicador do ataque básico");
        Equal(18, player.BaseAttackDamage, "alocação preserva base do nível");
        player.GrantExperience(475);
        Require(player.TryAllocate(HeroAttribute.Intelligence, 10), "aloca inteligência após evoluções");
        Require(player.TryAllocate(HeroAttribute.Defense, 5), "aloca defesa após evoluções");
        Near(1.15d, player.MagicDamageMultiplier, "inteligência soma um e meio por cento de dano mágico");
        Near(1.05d, player.MagicDurationMultiplier, "inteligência soma meio por cento de duração");
        Near(4.2d, player.GetMagicDuration(4d), "duração mágica multiplicada");
        Equal(65, player.GetSkillDamage(), "habilidade combina ataque e inteligência na base do nível");
        Equal(57, player.GetSkillDamage(1.8d, false), "habilidade física ignora inteligência");
        Near(100d / 110d, player.DamageTakenMultiplier, "defesa usa redução racional com retorno decrescente");
        Equal(9, player.ReceiveDamage(10), "defesa reduz dano recebido");
    }

    private static void CheckSingleDamageRounding()
    {
        PlayerProgression physical = new PlayerProgression();
        physical.TryAllocate(HeroAttribute.Attack, 2);
        Equal(25, physical.GetBasicDamage(1.35d), "bônus de dano usa valor físico sem arredondamento intermediário");
        Equal(45, physical.GetSkillDamage(1.8d, true, 1.35d), "bônus de habilidade arredonda uma única vez");
        PlayerProgression defense = new PlayerProgression();
        defense.TryAllocate(HeroAttribute.Defense, 5);
        Equal(1, defense.ReceiveDamage(3, 0.5d), "guarda e defesa combinadas arredondam uma única vez");
        Equal(1, defense.ReceiveDamage(1, 0.5d), "dano positivo muito pequeno conserva mínimo de um");
        Equal(3, CombatScaling.RoundDamage(2.5d), "meio ponto arredonda para cima");
        Equal(2, CombatScaling.RoundDamage(2.499d), "fração abaixo da metade arredonda para baixo");
    }

    private static void CheckScalingLimits()
    {
        PlayerProgression player = new PlayerProgression();
        Equal(0, player.GetBasicDamage(double.NaN), "NaN não causa dano");
        Equal(0, player.GetBasicDamage(-1d), "bônus negativo inválido não causa dano");
        Equal(0, player.GetSkillDamage(-1d, true, -1d), "dois multiplicadores negativos não viram dano positivo");
        Equal(0, player.GetSkillDamage(double.NaN), "poder inválido não causa dano");
        Equal(int.MaxValue, player.GetBasicDamage(double.MaxValue), "dano básico satura antes do overflow inteiro");
        Equal(int.MaxValue, player.GetSkillDamage(double.MaxValue, true, double.MaxValue), "dano de habilidade satura no overflow real");
        Equal(0, player.ReceiveDamage(int.MaxValue, double.NaN), "multiplicador de entrada inválido preserva vida");
        Equal(100, player.ReceiveDamage(int.MaxValue, double.MaxValue), "dano imenso limitado à vida existente");
        Equal(0, player.ReceiveDamage(1, double.PositiveInfinity), "dano não reaplicado após derrota");
        Near(0d, player.GetMagicDuration(-1d), "duração negativa recusada");
        Near(0d, player.GetMagicDuration(double.NaN), "duração NaN recusada");
        Require(player.GetMagicDuration(double.PositiveInfinity) == double.MaxValue, "duração infinita saturada em valor finito");
        player.GrantExperience(int.MaxValue);
        Require(player.TryAllocate(HeroAttribute.Attack, 250), "todos os pontos do limite alocáveis sem overflow");
        Equal(1284, player.GetBasicDamage(), "dano com ataque máximo");
        Equal(0, player.UnspentPoints, "limite não permite saldo negativo");
        Require(!player.TryAllocate(HeroAttribute.Defense), "atributo adicional sem saldo recusado no limite");
    }

    private static void CheckEqualPointBuilds()
    {
        PlayerProgression attack = new PlayerProgression();
        PlayerProgression defense = new PlayerProgression();
        PlayerProgression intelligence = new PlayerProgression();
        attack.GrantExperience(100); defense.GrantExperience(100); intelligence.GrantExperience(100);
        attack.TryAllocate(HeroAttribute.Attack, 10);
        defense.TryAllocate(HeroAttribute.Defense, 10);
        intelligence.TryAllocate(HeroAttribute.Intelligence, 10);
        Require(attack.GetBasicDamage() > intelligence.GetBasicDamage(), "mesmo investimento: ataque favorece golpes básicos");
        Require(intelligence.GetSkillDamage() > attack.GetSkillDamage(), "mesmo investimento: inteligência favorece habilidade mágica");
        Require(intelligence.GetMagicDuration(8d) > attack.GetMagicDuration(8d), "inteligência prolonga magia");
        Require(defense.ReceiveDamage(40) < attack.ReceiveDamage(40), "mesmo investimento: defesa reduz golpe recebido");
        Equal(120, intelligence.Health, "comparação de build não altera vida sem dano");
    }

    private static void CheckWeaponRequirements()
    {
        PlayerProgression player = new PlayerProgression();
        Equal(1, player.SwordProficiencyLevel, "proficiência inicial de espada");
        WeaponDefinition original = player.EquippedWeapon;
        Require(original == WeaponDefinition.TrainingSword, "arma atual disponível na criação");
        Require(player.TryEquipWeapon(original, out string reason) && reason.Length == 0, "requisitos iniciais atendidos");
        WeaponDefinition restricted = new WeaponDefinition("test-requirements", "Arma de teste",
            new WeaponRequirements(2, 3, 1, 1, 1));
        Require(!player.TryEquipWeapon(restricted, out reason) && reason.Contains("Nível 2"), "nível mínimo impede equipamento");
        Require(player.EquippedWeapon == original, "tentativa recusada preserva arma equipada");
        player.GrantExperience(100);
        Require(!player.TryEquipWeapon(restricted, out reason) && reason.Contains("Ataque 3") &&
            reason.Contains("Defesa 1") && reason.Contains("Inteligência 1"), "requisitos de cada atributo são independentes");
        player.TryAllocate(HeroAttribute.Attack, 3);
        player.TryAllocate(HeroAttribute.Defense);
        Require(!player.TryEquipWeapon(restricted, out reason) && reason.Contains("Inteligência 1"), "requisito restante não é ignorado");
        player.TryAllocate(HeroAttribute.Intelligence);
        Require(player.TryEquipWeapon(restricted, out reason) && player.EquippedWeapon == restricted,
            "arma equipável após cumprir todos os requisitos");
        WeaponDefinition proficiency = new WeaponDefinition("test-proficiency", "Arma de teste de proficiência",
            new WeaponRequirements(minimumSwordProficiency: 2));
        Require(!player.TryEquipWeapon(proficiency, out reason) && reason.Contains("Proficiência em espada 2"),
            "proficiência de espada é requisito distinto dos atributos");
        Require(player.EquippedWeapon == restricted, "proficiência insuficiente preserva equipamento anterior");
        Require(!player.TryEquipWeapon(null, out reason), "arma nula recusada");
        Require(restricted.Requirements.Describe().Contains("Ataque 3"), "descrição mostra requisitos estruturados");
    }

    private static void Near(double expected, double actual, string description)
    {
        Require(System.Math.Abs(expected - actual) < 0.000001d,
            $"{description}: esperado {expected}, recebido {actual}");
    }

    private static void Equal(int expected, int actual, string description)
    {
        Require(expected == actual, $"{description}: esperado {expected}, recebido {actual}");
    }

    private static void Require(bool condition, string description)
    {
        if (!condition)
            throw new BuildFailedException("Falha nas regras de combate: " + description);
    }
}
