#if UNITY_EDITOR
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Builds a bare defender (stamina, defense, health) for guard rule tests.
/// </summary>
internal static class DefenseTestRig
{
    public const float BlockCostPerDamage = 2f;
    public const float ParryWindow = 0.2f;
    public const float ParryRearmDelay = 0.5f;

    public static GameObject Create(string name, out StaminaConfigData staminaConfig)
    {
        var defender = new GameObject(name);
        defender.SetActive(false);
        staminaConfig = ScriptableObject.CreateInstance<StaminaConfigData>();
        staminaConfig.SetSerializedField("maxStamina", 100f);
        staminaConfig.SetSerializedField("recoveryPerSecond", 0f);
        staminaConfig.SetSerializedField("blockCostPerDamage", BlockCostPerDamage);
        var stamina = defender.AddComponent<PlayerStamina>();
        var staminaSO = new SerializedObject(stamina);
        staminaSO.FindProperty("config").objectReferenceValue = staminaConfig;
        staminaSO.ApplyModifiedPropertiesWithoutUndo();

        var defense = defender.AddComponent<PlayerDefense>();
        var defenseSO = new SerializedObject(defense);
        defenseSO.FindProperty("parryWindow").floatValue = ParryWindow;
        defenseSO.FindProperty("parryRearmDelay").floatValue = ParryRearmDelay;
        defenseSO.ApplyModifiedPropertiesWithoutUndo();

        defender.AddComponent<Health>();
        defender.SetActive(true);
        return defender;
    }
}

/// <summary>
/// Focused coverage for the guard's rules: the Parry Window, blocking at a
/// stamina cost, unblockable hits, the guard arc and the guard break.
/// </summary>
[Category(TestGroups.Combat)]
public class PlayerDefenseTests
{
    private GameObject player;
    private GameObject source;
    private StaminaConfigData staminaConfig;
    private Health health;
    private PlayerDefense defense;
    private PlayerStamina stamina;
    private int damagedEvents;
    private int parriedEvents;
    private int blockedEvents;
    private int guardBrokenEvents;

    [SetUp]
    public void SetUp()
    {
        damagedEvents = 0;
        parriedEvents = 0;
        blockedEvents = 0;
        guardBrokenEvents = 0;
        player = DefenseTestRig.Create("PlayerDefenseTest", out staminaConfig);
        defense = player.GetComponent<PlayerDefense>();
        health = player.GetComponent<Health>();
        stamina = player.GetComponent<PlayerStamina>();
        health.Damaged += _ => damagedEvents++;
        defense.Parried += _ => parriedEvents++;
        defense.Blocked += _ => blockedEvents++;
        defense.GuardBroken += _ => guardBrokenEvents++;

        source = new GameObject("AttackSource");
        source.transform.position = player.transform.position + player.transform.forward * 2f;
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(source);
        Object.DestroyImmediate(player);
        Object.DestroyImmediate(staminaConfig);
    }

    [Test]
    public void ParryableHitInsideTheParryWindow_IsParriedAtNoStaminaCost()
    {
        defense.RaiseGuard();
        defense.Tick(DefenseTestRig.ParryWindow * 0.5f);
        Assert.That(defense.IsParryWindowOpen, Is.True);

        Assert.That(Damage(parryable: true, blockable: true), Is.EqualTo(HitResult.Parried));

        Assert.That(health.CurrentHealth, Is.EqualTo(health.MaxHealth));
        Assert.That(stamina.CurrentStamina, Is.EqualTo(stamina.MaxStamina));
        Assert.That(parriedEvents, Is.EqualTo(1));
        Assert.That(blockedEvents, Is.Zero);
        Assert.That(defense.IsGuarding, Is.True, "A parry leaves the guard up.");
    }

    [Test]
    public void HitAfterTheParryWindow_IsBlockedAndCostsStaminaByItsDamage()
    {
        CloseParryWindow();

        Assert.That(Damage(parryable: true, blockable: true), Is.EqualTo(HitResult.Blocked));

        Assert.That(health.CurrentHealth, Is.EqualTo(health.MaxHealth));
        Assert.That(damagedEvents, Is.Zero);
        Assert.That(stamina.CurrentStamina, Is.EqualTo(stamina.MaxStamina - 10f * DefenseTestRig.BlockCostPerDamage));
        Assert.That(blockedEvents, Is.EqualTo(1));
        Assert.That(parriedEvents, Is.Zero);
        Assert.That(defense.IsGuarding, Is.True);
    }

    [Test]
    public void UnblockableHit_LandsThroughTheGuard_UnlessItIsParried()
    {
        defense.RaiseGuard();
        Assert.That(Damage(parryable: false, blockable: false), Is.EqualTo(HitResult.Applied),
            "A hit that is neither blockable nor parryable ignores the guard.");
        Assert.That(Damage(parryable: true, blockable: false), Is.EqualTo(HitResult.Parried),
            "An unblockable hit can still be parried inside the window.");

        defense.Tick(DefenseTestRig.ParryWindow + 0.01f);
        Assert.That(Damage(parryable: true, blockable: false), Is.EqualTo(HitResult.Applied),
            "Outside the window an unblockable hit lands through the guard.");

        Assert.That(health.CurrentHealth, Is.EqualTo(health.MaxHealth - 20f));
        Assert.That(stamina.CurrentStamina, Is.EqualTo(stamina.MaxStamina));
    }

    [Test]
    public void Guard_IgnoresHitsFromOutsideItsArc()
    {
        defense.RaiseGuard();
        source.transform.position = player.transform.position + player.transform.right * 2f;

        Assert.That(Damage(parryable: true, blockable: true), Is.EqualTo(HitResult.Applied));

        Assert.That(health.CurrentHealth, Is.EqualTo(health.MaxHealth - 10f));
        Assert.That(parriedEvents + blockedEvents, Is.Zero);
    }

    [Test]
    public void BlockedHitBeyondStamina_DealsNoDamage_EmptiesStamina_AndBreaksTheGuard()
    {
        CloseParryWindow();
        float amount = stamina.MaxStamina / DefenseTestRig.BlockCostPerDamage + 1f;

        HitResult result = health.TakeDamage(new DamageInfo(amount, source.transform, player.transform.position, blockable: true));

        Assert.That(result, Is.EqualTo(HitResult.Blocked));
        Assert.That(health.CurrentHealth, Is.EqualTo(health.MaxHealth));
        Assert.That(stamina.CurrentStamina, Is.Zero);
        Assert.That(stamina.IsExhausted, Is.True);
        Assert.That(guardBrokenEvents, Is.EqualTo(1));
        Assert.That(blockedEvents, Is.Zero, "A broken guard reports GuardBroken in place of Blocked.");
        Assert.That(defense.IsGuarding, Is.False);

        Assert.That(Damage(parryable: true, blockable: true), Is.EqualTo(HitResult.Applied),
            "The next hit lands on the broken guard.");
    }

    private void CloseParryWindow()
    {
        defense.RaiseGuard();
        defense.Tick(DefenseTestRig.ParryWindow + 0.01f);
        Assert.That(defense.IsGuarding, Is.True);
        Assert.That(defense.IsParryWindowOpen, Is.False);
    }

    private HitResult Damage(bool parryable = false, bool blockable = false)
    {
        return health.TakeDamage(new DamageInfo(10f, source.transform, player.transform.position, parryable, blockable: blockable));
    }
}
#endif
