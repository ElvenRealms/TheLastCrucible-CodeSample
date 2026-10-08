#if UNITY_EDITOR
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

[Category(TestGroups.Combat)]
public class HealthBoundaryTests
{
    private readonly List<GameObject> objects = new();

    [TearDown]
    public void TearDown()
    {
        foreach (GameObject gameObject in objects)
        {
            if (gameObject != null) Object.DestroyImmediate(gameObject);
        }

        objects.Clear();
    }

    [Test]
    public void InvalidMaxHealth_IsSanitizedToFinitePositiveBounds()
    {
        foreach (float invalidMaxHealth in new[] { 0f, -1f, float.NaN, float.PositiveInfinity })
        {
            Health health = CreateHealth(invalidMaxHealth);

            Assert.That(float.IsNaN(health.MaxHealth), Is.False);
            Assert.That(float.IsInfinity(health.MaxHealth), Is.False);
            Assert.That(health.MaxHealth, Is.GreaterThan(0f));
            Assert.That(health.CurrentHealth, Is.EqualTo(health.MaxHealth));
            Assert.That(health.IsDead, Is.False);
        }
    }

    [Test]
    public void InvalidDamage_IsIgnoredWithoutHealthEvents()
    {
        Health health = CreateHealth(100f);
        int damagedEvents = 0;
        int changedEvents = 0;
        int diedEvents = 0;
        health.Damaged += _ => damagedEvents++;
        health.Changed += (_, _) => changedEvents++;
        health.Died += () => diedEvents++;

        foreach (float invalidDamage in new[] { 0f, -1f, float.NaN, float.NegativeInfinity, float.PositiveInfinity })
        {
            Assert.That(health.TakeDamage(new DamageInfo(invalidDamage, null, health.transform.position)), Is.EqualTo(HitResult.Ignored));
        }

        Assert.That(health.CurrentHealth, Is.EqualTo(health.MaxHealth));
        Assert.That(damagedEvents, Is.Zero);
        Assert.That(changedEvents, Is.Zero);
        Assert.That(diedEvents, Is.Zero);
    }

    [Test]
    public void Damage_ClampsAtZeroAndPublishesEventsInOrder()
    {
        Health health = CreateHealth(100f);
        var events = new List<string>();
        health.Damaged += _ => events.Add("Damaged");
        health.Changed += (_, _) => events.Add("Changed");
        health.Died += () => events.Add("Died");

        Assert.That(health.TakeDamage(new DamageInfo(150f, null, health.transform.position)), Is.EqualTo(HitResult.Applied));
        Assert.That(health.TakeDamage(new DamageInfo(1f, null, health.transform.position)), Is.EqualTo(HitResult.Ignored),
            "A dead target must ignore further hits.");

        Assert.That(health.CurrentHealth, Is.Zero);
        Assert.That(health.CurrentHealth, Is.LessThanOrEqualTo(health.MaxHealth));
        Assert.That(events, Is.EqualTo(new[] { "Damaged", "Changed", "Died" }));
    }

    [Test]
    public void RestoreFull_ClampsToMaxAndPublishesCurrentBounds()
    {
        Health health = CreateHealth(100f);
        var changedValues = new List<Vector2>();
        health.Changed += (current, maximum) => changedValues.Add(new Vector2(current, maximum));

        health.TakeDamage(new DamageInfo(25f, null, health.transform.position));
        health.RestoreFull();

        Assert.That(health.CurrentHealth, Is.EqualTo(health.MaxHealth));
        Assert.That(health.CurrentHealth, Is.GreaterThanOrEqualTo(0f));
        Assert.That(changedValues.Count, Is.EqualTo(2));
        Assert.That(changedValues[0].x, Is.EqualTo(75f));
        Assert.That(changedValues[0].y, Is.EqualTo(100f));
        Assert.That(changedValues[1].x, Is.EqualTo(100f));
        Assert.That(changedValues[1].y, Is.EqualTo(100f));
    }

    [Test]
    public void DamageBlocks_FromSeparateOwners_DoNotClearEachOther()
    {
        Health health = CreateHealth(100f);
        var arenaOwner = new object();
        var bossOwner = new object();
        var hit = new DamageInfo(10f, null, health.transform.position);

        health.SetDamageBlocked(arenaOwner, true);
        health.SetDamageBlocked(bossOwner, true);
        health.SetDamageBlocked(bossOwner, false);
        Assert.That(health.AcceptsDamage, Is.False);
        Assert.That(health.TakeDamage(hit), Is.EqualTo(HitResult.Ignored));

        health.RestoreFull();
        Assert.That(health.AcceptsDamage, Is.False, "Restoring health must not lift a block.");

        health.SetDamageBlocked(arenaOwner, false);
        Assert.That(health.AcceptsDamage, Is.True);
        Assert.That(health.TakeDamage(hit), Is.EqualTo(HitResult.Applied));
    }

    private Health CreateHealth(float maxHealth)
    {
        var gameObject = new GameObject("HealthBoundaryTest");
        gameObject.SetActive(false);
        Health health = gameObject.AddComponent<Health>();
        var serializedObject = new SerializedObject(health);
        serializedObject.FindProperty("maxHealth").floatValue = maxHealth;
        serializedObject.ApplyModifiedPropertiesWithoutUndo();
        gameObject.SetActive(true);
        objects.Add(gameObject);
        return health;
    }
}
#endif
