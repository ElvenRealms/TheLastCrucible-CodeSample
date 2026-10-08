#if UNITY_EDITOR
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Coverage for how <see cref="TimeScaleArbiter"/> resolves claims and the base scale
/// into Time.timeScale and Time.fixedDeltaTime.
/// </summary>
[Category(TestGroups.Feedback)]
public class TimeScaleArbiterTests
{
    private float _defaultFixedDeltaTime;

    [SetUp]
    public void Setup()
    {
        TimeScaleArbiter.Reset();
        _defaultFixedDeltaTime = Time.fixedDeltaTime;
    }

    [TearDown]
    public void TearDown()
    {
        TimeScaleArbiter.Reset();
    }

    [Test]
    public void LowestClaim_Wins_AndReleaseRestoresTheNextLowest()
    {
        TimeScaleClaim slowMotion = TimeScaleArbiter.Claim(0.45f, null);
        TimeScaleClaim freeze = TimeScaleArbiter.Claim(0f, null);

        Assert.That(Time.timeScale, Is.Zero);

        freeze.Release();
        Assert.That(Time.timeScale, Is.EqualTo(0.45f).Within(0.0001f));

        slowMotion.Release();
        Assert.That(Time.timeScale, Is.EqualTo(1f).Within(0.0001f));
        Assert.That(TimeScaleArbiter.Claims, Is.Empty);
    }

    [Test]
    public void BaseScale_AppliesWithoutClaims_AndCapsHigherClaims()
    {
        TimeScaleArbiter.SetBaseScale(0.25f);
        Assert.That(Time.timeScale, Is.EqualTo(0.25f).Within(0.0001f));

        TimeScaleClaim slowMotion = TimeScaleArbiter.Claim(0.45f, null);
        Assert.That(Time.timeScale, Is.EqualTo(0.25f).Within(0.0001f));

        slowMotion.Release();
        TimeScaleArbiter.SetBaseScale(1f);
        Assert.That(Time.timeScale, Is.EqualTo(1f).Within(0.0001f));
    }

    [Test]
    public void DoubleRelease_DoesNotReleaseAnotherClaim()
    {
        TimeScaleClaim first = TimeScaleArbiter.Claim(0.5f, null);
        TimeScaleClaim second = TimeScaleArbiter.Claim(0.5f, null);

        first.Release();
        first.Release();

        Assert.That(first.IsActive, Is.False);
        Assert.That(second.IsActive, Is.True);
        Assert.That(Time.timeScale, Is.EqualTo(0.5f).Within(0.0001f));
    }

    [Test]
    public void FixedDeltaTime_ScalesWithSlowMotion_AndKeepsTheDefaultWhenFrozenOrFast()
    {
        TimeScaleClaim slowMotion = TimeScaleArbiter.Claim(0.5f, null);
        Assert.That(Time.fixedDeltaTime, Is.EqualTo(_defaultFixedDeltaTime * 0.5f).Within(0.00001f));

        TimeScaleClaim freeze = TimeScaleArbiter.Claim(0f, null);
        Assert.That(Time.fixedDeltaTime, Is.EqualTo(_defaultFixedDeltaTime).Within(0.00001f));

        freeze.Release();
        slowMotion.Release();
        TimeScaleArbiter.SetBaseScale(2f);
        Assert.That(Time.timeScale, Is.EqualTo(2f).Within(0.0001f));
        Assert.That(Time.fixedDeltaTime, Is.EqualTo(_defaultFixedDeltaTime).Within(0.00001f));
    }

    [Test]
    public void SetScale_ReResolvesTheEffectiveScale_AndTheLowestClaimStillWins()
    {
        TimeScaleClaim slowMotion = TimeScaleArbiter.Claim(0.5f, null);
        TimeScaleClaim other = TimeScaleArbiter.Claim(0.3f, null);
        Assert.That(Time.timeScale, Is.EqualTo(0.3f).Within(0.0001f));

        other.SetScale(0.8f);
        Assert.That(Time.timeScale, Is.EqualTo(0.5f).Within(0.0001f), "The other claim is no longer the lowest.");

        slowMotion.SetScale(0.1f);
        Assert.That(Time.timeScale, Is.EqualTo(0.1f).Within(0.0001f));
        Assert.That(slowMotion.Scale, Is.EqualTo(0.1f).Within(0.0001f));

        slowMotion.Release();
        Assert.That(Time.timeScale, Is.EqualTo(0.8f).Within(0.0001f));
        other.Release();
    }
}
#endif
