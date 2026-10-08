#if UNITY_EDITOR
using System.Collections;
using Animancer;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

[Category(TestGroups.Player)]
public class PlayerStaminaTests
{
    private GameObject player;
    private StaminaConfigData config;
    private PlayerStamina stamina;

    [TearDown]
    public void TearDown()
    {
        if (player != null) Object.DestroyImmediate(player);
        if (config != null) Object.DestroyImmediate(config);
    }

    [Test]
    public void Spend_WaitsForDelayThenRecoversRemainingFrameTime()
    {
        CreateStamina();
        config.SetSerializedField("recoveryDelay", 1f);
        config.SetSerializedField("recoveryPerSecond", 10f);

        Assert.That(stamina.TrySpend(StaminaAction.Dodge, 20f), Is.True);
        stamina.Tick(0.75f);
        Assert.That(stamina.CurrentStamina, Is.EqualTo(80f).Within(0.001f));

        stamina.Tick(0.5f);
        Assert.That(stamina.CurrentStamina, Is.EqualTo(82.5f).Within(0.001f),
            "Recovery should use only the portion of the frame after the delay expires.");
    }

    [Test]
    public void SprintExhaustion_ConsumesRemainderAndRequiresRecoveryAndRelease()
    {
        CreateStamina(maximum: 10f);
        config.SetSerializedField("recoveryDelay", 0f);
        config.SetSerializedField("recoveryPerSecond", 10f);
        config.SetSerializedField("sprintResumeThreshold", 5f);

        Assert.That(stamina.TrySpend(StaminaAction.Sprint, 12f), Is.False);
        Assert.That(stamina.CurrentStamina, Is.Zero);
        Assert.That(stamina.IsExhausted, Is.True);
        Assert.That(stamina.CanSprint, Is.False);

        stamina.Tick(1f);
        Assert.That(stamina.CurrentStamina, Is.EqualTo(10f));
        Assert.That(stamina.IsExhausted, Is.True,
            "Holding sprint must not restart it as soon as the threshold is crossed.");

        stamina.NotifySprintReleased();
        Assert.That(stamina.IsExhausted, Is.False);
        Assert.That(stamina.CanSprint, Is.True);
    }

    [Test]
    public void RestoreFull_ClearsRecoveryAndExhaustion()
    {
        CreateStamina(maximum: 10f);
        stamina.TrySpend(StaminaAction.Sprint, 10f);

        stamina.RestoreFull();

        Assert.That(stamina.CurrentStamina, Is.EqualTo(10f));
        Assert.That(stamina.IsExhausted, Is.False);
        Assert.That(stamina.CanSprint, Is.True);
    }

    private void CreateStamina(float maximum = 100f)
    {
        config = ScriptableObject.CreateInstance<StaminaConfigData>();
        config.SetSerializedField("maxStamina", maximum);
        player = new GameObject("PlayerStaminaTest");
        player.SetActive(false);
        stamina = player.AddComponent<PlayerStamina>();
        var serializedStamina = new SerializedObject(stamina);
        serializedStamina.FindProperty("config").objectReferenceValue = config;
        serializedStamina.ApplyModifiedPropertiesWithoutUndo();
        player.SetActive(true);
    }
}

[Category(TestGroups.Player)]
public class PlayerStaminaActionTests : InputTestFixture
{
    private const string PlayerPrefabPath = "Assets/Prefabs/Player/Player.prefab";

    private GameObject cameraGO;
    private GameObject ground;
    private GameObject root;
    private InputReader inputReader;
    private PlayerAnimationData animationData;
    private StaminaConfigData staminaConfig;
    private PlayerStateMachine machine;
    private GameObject audioRoot;
    private AnimancerState CurrentAnim => machine.Animancer.Layers[0].CurrentState;

    public override void Setup()
    {
        base.Setup();
        audioRoot = TestAudioRoot.Create();
    }

    public override void TearDown()
    {
        TestAudioRoot.Destroy(audioRoot);
        if (root != null) Object.DestroyImmediate(root);
        if (ground != null) Object.DestroyImmediate(ground);
        if (cameraGO != null) Object.DestroyImmediate(cameraGO);
        if (inputReader != null) Object.DestroyImmediate(inputReader);
        if (animationData != null)
        {
            foreach (var attack in animationData.LightCombo)
            {
                Object.DestroyImmediate(attack);
            }
        }
        if (animationData != null) Object.DestroyImmediate(animationData);
        if (staminaConfig != null) Object.DestroyImmediate(staminaConfig);
        base.TearDown();
    }

    [UnityTest]
    public IEnumerator DiscreteActions_SpendConfiguredCostsAndRejectWithoutTransition()
    {
        yield return SpawnPlayer();

        machine.CurrentState.OnInput(PlayerAction.Attack);
        Assert.That(machine.CurrentState, Is.InstanceOf<AttackState>());
        Assert.That(machine.Stamina.CurrentStamina, Is.EqualTo(90f).Within(0.001f));

        machine.ForceChangeState(machine.Locomotion);
        machine.CurrentState.OnInput(PlayerAction.Dodge);
        Assert.That(machine.CurrentState, Is.InstanceOf<DodgeState>());
        Assert.That(machine.Stamina.CurrentStamina, Is.EqualTo(65f).Within(0.001f));

        machine.ForceChangeState(machine.Locomotion);
        machine.CurrentState.OnInput(PlayerAction.Block);
        Assert.That(machine.CurrentState, Is.InstanceOf<BlockState>());
        Assert.That(machine.Stamina.CurrentStamina, Is.EqualTo(65f).Within(0.001f),
            "Raising the guard is free; only a blocked hit costs stamina.");

        machine.ForceChangeState(machine.Locomotion);
        machine.Stamina.TrySpend(StaminaAction.Attack, 48f);
        int rejections = 0;
        machine.Stamina.SpendRejected += _ => rejections++;
        machine.CurrentState.OnInput(PlayerAction.Dodge);

        Assert.That(machine.CurrentState, Is.InstanceOf<LocomotionState>(),
            "An unaffordable dodge must not leave locomotion.");
        Assert.That(machine.Stamina.CurrentStamina, Is.EqualTo(17f).Within(0.001f));
        Assert.That(rejections, Is.EqualTo(1));
    }

    [UnityTest]
    public IEnumerator AttackCancels_SpendAffordableDodgeAndRefuseBlockWhenExhausted()
    {
        yield return SpawnPlayer();

        machine.CurrentState.OnInput(PlayerAction.Attack);
        CurrentAnim.NormalizedTime = animationData.LightCombo[0].CancelWindowStart;
        machine.CurrentState.OnInput(PlayerAction.Dodge);

        Assert.That(machine.CurrentState, Is.InstanceOf<DodgeState>());
        Assert.That(machine.Stamina.CurrentStamina, Is.EqualTo(65f).Within(0.001f),
            "Starting an attack and cancelling to Dodge must spend both configured costs.");

        machine.ForceChangeState(machine.Locomotion);
        machine.Stamina.RestoreFull();
        machine.CurrentState.OnInput(PlayerAction.Attack);
        Assert.That(machine.Stamina.TrySpend(StaminaAction.Attack, 90f), Is.True);
        Assert.That(machine.Stamina.IsExhausted, Is.True);
        int rejections = 0;
        machine.Stamina.SpendRejected += _ => rejections++;
        CurrentAnim.NormalizedTime = animationData.LightCombo[0].CancelWindowStart;

        machine.CurrentState.OnInput(PlayerAction.Block);

        Assert.That(machine.CurrentState, Is.InstanceOf<AttackState>(),
            "An exhausted player cannot cancel into a guard.");
        Assert.That(rejections, Is.Zero,
            "The guard refuses silently, because a held Block asks again every frame.");
    }

    private IEnumerator SpawnPlayer()
    {
        cameraGO = new GameObject("TestCamera", typeof(Camera));
        cameraGO.tag = "MainCamera";
        ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.transform.localScale = new Vector3(10f, 1f, 10f);
        inputReader = ScriptableObject.CreateInstance<InputReader>();

        root = new GameObject("TestRoot");
        root.SetActive(false);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        var player = Object.Instantiate(prefab, root.transform);
        player.transform.position = new Vector3(0f, 0.1f, 0f);
        machine = player.GetComponentInChildren<PlayerStateMachine>();
        Assert.That(machine.Stamina, Is.Null,
            "PlayerStateMachine Awake should not run before the inactive test root activates.");

        var machineSO = new SerializedObject(machine);
        var sourceAnimationData = (PlayerAnimationData)machineSO.FindProperty("animationData").objectReferenceValue;
        animationData = Object.Instantiate(sourceAnimationData);
        for (int i = 0; i < animationData.LightCombo.Length; i++)
        {
            animationData.LightCombo[i] = Object.Instantiate(sourceAnimationData.LightCombo[i]);
            animationData.LightCombo[i].SetSerializedField("staminaCost", 10f);
        }
        machineSO.FindProperty("inputReader").objectReferenceValue = inputReader;
        machineSO.FindProperty("animationData").objectReferenceValue = animationData;
        machineSO.ApplyModifiedPropertiesWithoutUndo();

        var stamina = player.GetComponent<PlayerStamina>();
        Assert.That(stamina, Is.Not.Null, "The Player prefab must include PlayerStamina.");
        var staminaSO = new SerializedObject(stamina);
        Assert.That(staminaSO.FindProperty("config").objectReferenceValue, Is.Not.Null,
            "The Player prefab must reference its authored stamina config.");
        staminaConfig = ScriptableObject.CreateInstance<StaminaConfigData>();
        staminaConfig.SetSerializedField("maxStamina", 100f);
        staminaConfig.SetSerializedField("recoveryPerSecond", 0f);
        staminaConfig.SetSerializedField("recoveryDelay", 0f);
        staminaConfig.SetSerializedField("dodgeCost", 25f);
        staminaSO.FindProperty("config").objectReferenceValue = staminaConfig;
        staminaSO.ApplyModifiedPropertiesWithoutUndo();

        root.SetActive(true);
        float timeout = Time.time + 2f;
        while (!machine.Motor.IsGrounded && Time.time < timeout) yield return null;
        Assert.That(machine.Motor.IsGrounded, Is.True, "Player never landed on the test ground.");
        Assert.That(machine.CurrentState, Is.InstanceOf<LocomotionState>());
    }
}
#endif
