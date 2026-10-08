#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Animancer;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

/// <summary>
/// Prefab-level coverage for the boss's rare transitions: death over stagger,
/// poise breaks, cooldowns and the reset after the player dies.
/// </summary>
[Category(TestGroups.Boss)]
public class BossStateMachineTests : InputTestFixture
{
    private const string BossPrefabPath = "Assets/Prefabs/Enemies/Boss Demon.prefab";
    private const string PlayerPrefabPath = "Assets/Prefabs/Player/Player.prefab";
    // Under half the test boss's 50 Poise, so one parry never breaks it.
    private const float ParryPoiseDamage = 20f;
    // The priority the authored attack options share, above turning and moving and below Enrage.
    private const int AttackPriority = 3;

    private GameObject arena;
    private GameObject cameraGO;
    private GameObject root;
    private InputReader inputReader;
    private ArenaManager arenaManager;
    private BossStateMachine boss;
    private BossAnimationData bossAnimationData;
    private BossOptionSet bossOptionSet;
    private PoiseConfigData bossPoiseConfig;
    private PlayerStateMachine player;
    private Keyboard keyboard;
    private NavMeshData resetNavMeshData;
    private NavMeshDataInstance resetNavMeshInstance;
    private GameObject audioRoot;
    private readonly List<Object> attackObjects = new List<Object>();
    private readonly Dictionary<BossAttackDefinition, BossOptionEntry> attackEntries = new Dictionary<BossAttackDefinition, BossOptionEntry>();

    public override void Setup()
    {
        base.Setup();
        keyboard = InputSystem.AddDevice<Keyboard>();
        audioRoot = TestAudioRoot.Create();
    }

    public override void TearDown()
    {
        TestAudioRoot.Destroy(audioRoot);
        TimeScaleArbiter.Reset();
        if (resetNavMeshInstance.valid) resetNavMeshInstance.Remove();
        if (resetNavMeshData != null) Object.Destroy(resetNavMeshData);
        if (root != null) Object.Destroy(root);
        if (arena != null) Object.Destroy(arena);
        if (cameraGO != null) Object.Destroy(cameraGO);
        if (inputReader != null) Object.Destroy(inputReader);
        if (bossAnimationData != null) Object.Destroy(bossAnimationData);
        if (bossOptionSet != null) Object.Destroy(bossOptionSet);
        if (bossPoiseConfig != null) Object.Destroy(bossPoiseConfig);
        foreach (var attackObject in attackObjects)
        {
            Object.DestroyImmediate(attackObject);
        }
        attackObjects.Clear();
        attackEntries.Clear();
        base.TearDown();
    }

    // Every encounter reset rebinds the boss to its authored pose on the NavMesh,
    // so each fixture carries an isolated one; the agent stays disabled until then.
    private void BuildResetNavMesh()
    {
        NavMeshAgent agent = boss.GetComponent<NavMeshAgent>();
        NavMeshBuildSettings settings = NavMesh.GetSettingsByID(agent.agentTypeID);
        var sources = new List<NavMeshBuildSource>
        {
            new NavMeshBuildSource
            {
                shape = NavMeshBuildSourceShape.Box,
                size = new Vector3(20f, 0.5f, 20f),
                transform = Matrix4x4.TRS(new Vector3(0f, -0.25f, 0f), Quaternion.identity, Vector3.one),
                area = 0
            }
        };

        resetNavMeshData = NavMeshBuilder.BuildNavMeshData(
            settings,
            sources,
            new Bounds(Vector3.zero, new Vector3(30f, 4f, 30f)),
            Vector3.zero,
            Quaternion.identity);
        Assert.That(resetNavMeshData, Is.Not.Null, "Reset fixture failed to build its isolated NavMesh.");
        resetNavMeshInstance = NavMesh.AddNavMeshData(resetNavMeshData);
        Assert.That(resetNavMeshInstance.valid, Is.True, "Reset fixture failed to register its isolated NavMesh.");
        NavMeshHit authoredHit;
        Assert.That(NavMesh.SamplePosition(boss.AuthoredPosition, out authoredHit, 1f, agent.areaMask), Is.True,
            "The authored boss pose must be on the isolated reset NavMesh.");
    }

    private IEnumerator SpawnEncounter(float targetDistance, System.Action<BossOptionSet> configureAttacks = null, float staggerLoopDuration = 2f)
    {
        cameraGO = new GameObject("TestCamera", typeof(Camera));
        cameraGO.tag = "MainCamera";
        inputReader = ScriptableObject.CreateInstance<InputReader>();

        arena = new GameObject("TestArena");
        arena.SetActive(false);
        var zone = arena.AddComponent<BoxCollider>();
        zone.isTrigger = true;
        zone.size = new Vector3(30f, 4f, 30f);
        var body = arena.AddComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
        arenaManager = arena.AddComponent<ArenaManager>();
        var activationZone = arena.AddComponent<ArenaActivationZone>();
        var zoneSO = new SerializedObject(activationZone);
        zoneSO.FindProperty("arenaManager").objectReferenceValue = arenaManager;
        zoneSO.FindProperty("inputReader").objectReferenceValue = inputReader;
        zoneSO.ApplyModifiedPropertiesWithoutUndo();
        arena.SetActive(true);

        root = new GameObject("TestRoot");
        root.SetActive(false);

        var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        var playerInstance = Object.Instantiate(playerPrefab, root.transform);
        playerInstance.transform.position = new Vector3(0f, 0.1f, targetDistance);
        player = playerInstance.GetComponentInChildren<PlayerStateMachine>();
        var playerSO = new SerializedObject(player);
        playerSO.FindProperty("inputReader").objectReferenceValue = inputReader;
        playerSO.FindProperty("arenaManager").objectReferenceValue = arenaManager;
        playerSO.ApplyModifiedPropertiesWithoutUndo();

        var bossPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BossPrefabPath);
        var bossInstance = Object.Instantiate(bossPrefab, root.transform);
        // Face the player's +Z spawn regardless of the prefab root's authored placement.
        bossInstance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        bossInstance.GetComponent<NavMeshAgent>().enabled = false;
        boss = bossInstance.GetComponent<BossStateMachine>();
        var bossSO = new SerializedObject(boss);
        bossOptionSet = Object.Instantiate((BossOptionSet)bossSO.FindProperty("optionSet").objectReferenceValue);
        RemoveOptions(bossOptionSet, entry => entry.Action is BossEnrageAction);
        foreach (var attack in Attacks(bossOptionSet))
        {
            SetAttackField(attack, "openingAttack", CloneAttack(attack.OpeningAttack));
            for (int i = 0; i < attack.FollowUpAttacks.Length; i++)
            {
                attack.FollowUpAttacks[i] = CloneAttack(attack.FollowUpAttacks[i]);
            }
        }
        MakeAttacksDeterministic(bossOptionSet);
        configureAttacks?.Invoke(bossOptionSet);
        var sourceAnimationData = (BossAnimationData)bossSO.FindProperty("animationData").objectReferenceValue;
        bossAnimationData = Object.Instantiate(sourceAnimationData);
        var animationDataSO = new SerializedObject(bossAnimationData);
        animationDataSO.FindProperty("staggerLoopDuration").floatValue = staggerLoopDuration;
        animationDataSO.ApplyModifiedPropertiesWithoutUndo();
        bossSO.FindProperty("arenaManager").objectReferenceValue = arenaManager;
        bossSO.FindProperty("optionSet").objectReferenceValue = bossOptionSet;
        bossSO.FindProperty("animationData").objectReferenceValue = bossAnimationData;
        bossSO.ApplyModifiedPropertiesWithoutUndo();

        // The prefab carries Poise; fall back to adding one so the rig still
        // stands up before the prefab is wired. Either way the test config
        // must land before the boss's own Awake runs on activation below.
        Poise bossPoise = bossInstance.GetComponent<Poise>();
        if (bossPoise == null) bossPoise = bossInstance.AddComponent<Poise>();
        bossPoiseConfig = ScriptableObject.CreateInstance<PoiseConfigData>();
        bossPoiseConfig.SetSerializedField("maxPoise", 50f);
        bossPoiseConfig.SetSerializedField("recoveryDelay", 10f);
        bossPoiseConfig.SetSerializedField("recoveryPerSecond", 0f);
        var bossPoiseSO = new SerializedObject(bossPoise);
        bossPoiseSO.FindProperty("config").objectReferenceValue = bossPoiseConfig;
        bossPoiseSO.ApplyModifiedPropertiesWithoutUndo();

        root.SetActive(true);
        boss.SkipIntro();
        BuildResetNavMesh();
        yield return null;
        Physics.SyncTransforms();
        yield return new WaitForFixedUpdate();
    }

    private void PressInteract()
    {
        Press(keyboard.eKey);
        Release(keyboard.eKey);
    }

    // Builds an attack and the option that offers it, ranked with the authored attacks.
    private BossAttackDefinition CreateAttack(float minimumRange, float maximumRange, float weight = 1f)
    {
        var clip = new AnimationClip();
        attackObjects.Add(clip);
        var swing = ScriptableObject.CreateInstance<BossAttackData>();
        attackObjects.Add(swing);
        swing.SetSerializedField("animationClip", clip);
        var attack = new BossAttackDefinition();
        SetAttackField(attack, "openingAttack", swing);
        var conditions = new BossCondition[]
        {
            new BossRangeCondition(minimumRange, maximumRange),
            new BossFacingCondition(35f),
            new BossSituationCondition(BossSituation.AttackDelayElapsed)
        };
        attackEntries[attack] = new BossOptionEntry(null, AttackPriority, new BossOptionScoring(weight, conditions), new BossAttackAction(attack));
        return attack;
    }

    private static List<BossOptionEntry> AttackEntries(BossOptionSet data)
    {
        var entries = new List<BossOptionEntry>();
        foreach (var entry in data.Options)
        {
            if (entry.Action is BossAttackAction) entries.Add(entry);
        }
        return entries;
    }

    private static List<BossAttackDefinition> Attacks(BossOptionSet data)
    {
        return AttackEntries(data).ConvertAll(entry => ((BossAttackAction)entry.Action).Attack);
    }

    // Replaces every attack option with the ones built by CreateAttack, leaving the other options as authored.
    private void SetAttacks(BossOptionSet data, params BossAttackDefinition[] attacks)
    {
        RemoveOptions(data, entry => entry.Action is BossAttackAction);
        var options = new List<BossOptionEntry>(data.Options);
        foreach (var attack in attacks)
        {
            options.Add(attackEntries[attack]);
        }
        data.SetSerializedField("options", options.ToArray());
    }

    private static void RemoveOptions(BossOptionSet data, System.Predicate<BossOptionEntry> match)
    {
        var options = new List<BossOptionEntry>(data.Options);
        options.RemoveAll(match);
        data.SetSerializedField("options", options.ToArray());
    }

    private void AddCondition(BossAttackDefinition attack, BossCondition condition)
    {
        BossOptionScoring scoring = attackEntries[attack].Scoring;
        FieldInfo field = typeof(BossOptionScoring).GetField("conditions", BindingFlags.Instance | BindingFlags.NonPublic);
        var conditions = new List<BossCondition>((BossCondition[])field.GetValue(scoring)) { condition };
        field.SetValue(scoring, conditions.ToArray());
    }

    private BossAttackOption AttackOption(BossAttackDefinition attack)
    {
        foreach (var option in boss.AttackOptions)
        {
            if (option.Attack == attack) return option;
        }
        return null;
    }

    private BossAttackData CloneAttack(BossAttackData source)
    {
        var attack = Object.Instantiate(source);
        attackObjects.Add(attack);
        return attack;
    }

    private static void SetAttackField<T>(BossAttackDefinition attack, string fieldName, T value)
    {
        FieldInfo field = typeof(BossAttackDefinition).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, fieldName);
        field.SetValue(attack, value);
    }

    // The authored set also holds a non-parryable heavy that is eligible at this
    // range, so every opening is made parryable to keep the pick deterministic.
    private IEnumerator SpawnAndActivateAttack(float staggerLoopDuration = 2f)
    {
        yield return SpawnEncounter(1.5f, MakeOpeningsParryable, staggerLoopDuration);
        PressInteract();

        float attackDeadline = Time.time + 2f;
        while (!(boss.CurrentState is BossAttackState) && Time.time < attackDeadline)
            yield return null;
        Assert.That(boss.CurrentState, Is.InstanceOf<BossAttackState>(), "Boss never entered its in-range attack.");
    }

    // The authored options roll for combo length, chained attacks, reactions and ranged attacks; tests opt back in.
    private static void MakeAttacksDeterministic(BossOptionSet data)
    {
        foreach (BossPacing pacing in new[] { data.GetPacing(false), data.GetPacing(true) })
        {
            pacing.SetSerializedField("chainChance", 0f);
            pacing.SetSerializedField("healReactionChance", 0f);
            pacing.SetSerializedField("minimumRetaliationHits", 0);
            pacing.SetSerializedField("maximumRetaliationHits", 0);
        }
        foreach (var entry in AttackEntries(data))
        {
            BossAttackDefinition attack = ((BossAttackAction)entry.Action).Attack;
            SetAttackField(attack, "continueChance", 1f);
            SetAttackField(attack, "continueMaximumRange", 0f);
            if (attack.OpeningAttack.Projectile != null)
            {
                entry.Scoring.SetSerializedField("weight", 0f);
            }
        }
    }

    // Replaces the set with one swing on the boss's own clip, so a test can scrub it to any time.
    private BossAttackDefinition UseSingleSwing(BossOptionSet data)
    {
        AnimationClip clip = Attacks(data)[0].OpeningAttack.AnimationClip;
        BossAttackDefinition definition = CreateAttack(0f, 3f);
        definition.OpeningAttack.SetSerializedField("animationClip", clip);
        SetAttacks(data, definition);
        return definition;
    }

    private IEnumerator WaitForAttackState()
    {
        float attackDeadline = Time.time + 2f;
        while (!(boss.CurrentState is BossAttackState) && Time.time < attackDeadline)
            yield return null;
        Assert.That(boss.CurrentState, Is.InstanceOf<BossAttackState>(), "Boss never entered its configured attack.");
    }

    private void ScrubAttackTo(float normalizedTime)
    {
        boss.GetComponent<AnimancerComponent>().Layers[0].CurrentState.NormalizedTime = normalizedTime;
        boss.Attack.Tick();
    }

    private static void MakeOpeningsParryable(BossOptionSet data)
    {
        foreach (var attack in Attacks(data))
        {
            attack.OpeningAttack.SetSerializedField("parryable", true);
            attack.OpeningAttack.SetSerializedField("parryPoiseDamage", ParryPoiseDamage);
        }
    }

    // Empties the boss's Poise without a hit, the way a run of parries does.
    private void BreakBossPoise()
    {
        boss.Poise.TakeDamage(boss.Poise.Max, player.transform);
    }

    private void SetAttackHitWindow()
    {
        AnimancerState animationState = boss.GetComponent<AnimancerComponent>().Layers[0].CurrentState;
        animationState.NormalizedTime = boss.Attack.ActiveStep.HitWindowStart + 0.01f;
        boss.Attack.Tick();
    }

    [UnityTest]
    public IEnumerator Cooldown_KeepsTheAttackOutOfSelectionAfterItsUse()
    {
        BossAttackDefinition attack = null;
        yield return SpawnEncounter(1.5f, data =>
        {
            attack = UseSingleSwing(data);
            AddCondition(attack, new BossCooldownCondition(30f));
            data.GetPacing(false).SetSerializedField("minimumAttackDelay", 0f);
            data.GetPacing(false).SetSerializedField("maximumAttackDelay", 0f);
            data.GetPacing(false).SetSerializedField("breatherChance", 0f);
        });
        PressInteract();
        yield return WaitForAttackState();

        ScrubAttackTo(1f);
        Assert.That(boss.CurrentState, Is.SameAs(boss.Pursue));
        yield return new WaitForSeconds(0.3f);

        Assert.That(boss.CurrentState, Is.SameAs(boss.Pursue), "The only attack is on cooldown.");
        Assert.That(boss.HasAttackOpportunity(), Is.False, "An attack on cooldown is not an opportunity.");
        Assert.That(AttackOption(attack).Memory.SecondsSinceStarted, Is.LessThan(30f));

        Assert.That(boss.ResetForEncounter(), Is.True);
        Assert.That(float.IsPositiveInfinity(AttackOption(attack).Memory.SecondsSinceStarted), Is.True, "A new attempt starts with no cooldown running.");
    }

    [UnityTest]
    public IEnumerator Stagger_DeathHasPriorityOverLoopTimer()
    {
        yield return SpawnAndActivateAttack(staggerLoopDuration: 0.2f);
        SetAttackHitWindow();
        BreakBossPoise();
        Assert.That(boss.CurrentState, Is.SameAs(boss.Stagger));

        boss.Health.TakeDamage(new DamageInfo(boss.Health.MaxHealth, null, boss.transform.position));
        Assert.That(boss.CurrentState, Is.InstanceOf<BossDeadState>());
        yield return new WaitForSeconds(0.35f);
        Assert.That(boss.CurrentState, Is.InstanceOf<BossDeadState>());
    }

    [UnityTest]
    public IEnumerator PlayerDeath_BossStandsDownInPlace_ThenResetsWithoutMemoryOnRespawn()
    {
        yield return SpawnAndActivateAttack();
        AssignPlayerRespawnPoint();
        var phases = new List<EncounterPhase> { arenaManager.Phase };
        int resetCount = 0;
        arenaManager.PhaseChanged += phases.Add;
        arenaManager.EncounterReset += () => resetCount++;

        boss.ChangeState(boss.Pursue);
        boss.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
        Assert.That(boss.TryStartCombatTurn(), Is.True);
        Vector3 standDownPosition = new Vector3(1f, 0f, -1f);
        boss.transform.position = standDownPosition;
        boss.Health.TakeDamage(new DamageInfo(10f, null, boss.transform.position));
        Assert.That(boss.PreviousAttack, Is.Not.Null);
        Assert.That(boss.IsCombatPivotArmed, Is.False);

        player.Health.TakeDamage(new DamageInfo(player.Health.MaxHealth, null, player.transform.position));

        Assert.That(phases, Is.EqualTo(new[] { EncounterPhase.Active, EncounterPhase.Idle }));
        Assert.That(resetCount, Is.Zero, "The boss reset must wait for the respawn.");
        Assert.That(arenaManager.IsResetPending, Is.True);
        Assert.That(boss.CurrentState, Is.SameAs(boss.Dormant));
        Assert.That(boss.transform.position, Is.EqualTo(standDownPosition));
        Assert.That(boss.Health.CurrentHealth, Is.EqualTo(boss.Health.MaxHealth - 10f));

        player.Respawn();

        Assert.That(resetCount, Is.EqualTo(1));
        Assert.That(arenaManager.IsResetPending, Is.False);
        AssertBossReset();
    }

    private void AssignPlayerRespawnPoint()
    {
        var respawnObject = new GameObject("TestRespawnPoint", typeof(RespawnPoint));
        respawnObject.transform.SetParent(root.transform);
        respawnObject.transform.SetPositionAndRotation(player.transform.position, Quaternion.identity);
        var playerSO = new SerializedObject(player);
        playerSO.FindProperty("respawnPoint").objectReferenceValue = respawnObject.GetComponent<RespawnPoint>();
        playerSO.ApplyModifiedPropertiesWithoutUndo();
    }

    private void AssertBossReset()
    {
        Assert.That(arenaManager.Phase, Is.EqualTo(EncounterPhase.Idle));
        Assert.That(boss.CurrentState, Is.SameAs(boss.Dormant));
        Assert.That(boss.transform.position, Is.EqualTo(boss.AuthoredPosition));
        Assert.That(boss.transform.rotation, Is.EqualTo(boss.AuthoredRotation));
        Assert.That(boss.Health.CurrentHealth, Is.EqualTo(boss.Health.MaxHealth));
        Assert.That(boss.Health.AcceptsDamage, Is.True);
        Assert.That(boss.Poise.Current, Is.EqualTo(boss.Poise.Max));
        Assert.That(boss.Target, Is.Null);
        Assert.That(boss.PreviousAttack, Is.Null);
        Assert.That(boss.AttackDelayRemaining, Is.Zero);
        Assert.That(boss.IsCombatPivotArmed, Is.True);
        Assert.That(boss.Hitbox.IsWindowActive, Is.False);
    }

    [UnityTest]
    public IEnumerator PoiseBreak_FromAttack_EntersStaggerWithHitWindowClosed()
    {
        yield return SpawnAndActivateAttack();
        SetAttackHitWindow();
        Assert.That(boss.Hitbox.IsWindowActive, Is.True);

        boss.Health.TakeDamage(new DamageInfo(1f, player.transform, boss.transform.position, poiseDamage: 50f));

        Assert.That(boss.CurrentState, Is.SameAs(boss.Stagger));
        Assert.That(boss.Hitbox.IsWindowActive, Is.False);
    }

    [UnityTest]
    public IEnumerator LethalHitDuringAccumulation_EntersDeadNotStagger()
    {
        yield return SpawnEncounter(8f);
        PressInteract();
        yield return null;
        Assert.That(boss.CurrentState, Is.InstanceOf<BossPursueState>());

        boss.Health.TakeDamage(new DamageInfo(boss.Health.MaxHealth, null, boss.transform.position, poiseDamage: 50f));

        Assert.That(boss.CurrentState, Is.InstanceOf<BossDeadState>());
        Assert.That(boss.CurrentState, Is.Not.SameAs(boss.Stagger));
    }
}
#endif
