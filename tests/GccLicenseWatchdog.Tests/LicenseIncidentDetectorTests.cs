using GccLicenseWatchdog.Detection;
using GccLicenseWatchdog.Guardant;

namespace GccLicenseWatchdog.Tests;

public sealed class LicenseIncidentDetectorTests
{
    private static readonly FeatureKey FirstKey = new(100, 200, 1, 9, 2);
    private static readonly FeatureKey SecondKey = new(100, 200, 1, 9, 4);

    [Fact]
    public void FreeResourceDoesNotCreateCandidate()
    {
        var detector = new LicenseIncidentDetector();

        var report = detector.Evaluate([Feature(FirstKey, floating: 1)], []);

        Assert.Empty(report.ExhaustedFeatures);
        Assert.Empty(report.RestartCandidates);
    }

    [Fact]
    public void ExhaustedRemoteFeatureIsIgnored()
    {
        var detector = new LicenseIncidentDetector();

        var report = detector.Evaluate(
            [Feature(FirstKey, floating: 0, isRemote: true)],
            [Session(1, FirstKey, "42", "User")]);

        Assert.Empty(report.ExhaustedFeatures);
        Assert.Empty(report.RestartCandidates);
    }

    [Fact]
    public void ExhaustedLocalFeatureWithUniqueUsersIsDiagnosticOnly()
    {
        var detector = new LicenseIncidentDetector();

        var report = detector.Evaluate(
            [Feature(FirstKey, floating: 0)],
            [Session(1, FirstKey, "42", "First"), Session(2, FirstKey, "43", "Second")]);

        Assert.Single(report.ExhaustedFeatures);
        Assert.Empty(report.RestartCandidates);
    }

    [Fact]
    public void DuplicateUserIdInSameFeatureCreatesCandidate()
    {
        var detector = new LicenseIncidentDetector();

        var report = detector.Evaluate(
            [Feature(FirstKey, floating: 0)],
            [Session(10, FirstKey, "42", "First"), Session(20, FirstKey, "42", "First renamed")]);

        var candidate = Assert.Single(report.RestartCandidates);
        Assert.Equal(FirstKey, candidate.Feature.Key);
        Assert.Equal(2, candidate.SessionCount);
        var duplicate = Assert.Single(candidate.DuplicateUsers);
        Assert.Equal("id:42", duplicate.Identity);
        Assert.Equal([10L, 20L], duplicate.SessionIds);
    }

    [Fact]
    public void SameUserAcrossDifferentFeaturesDoesNotCreateCandidate()
    {
        var detector = new LicenseIncidentDetector();

        var report = detector.Evaluate(
            [Feature(FirstKey, 0), Feature(SecondKey, 0)],
            [Session(1, FirstKey, "42", "User"), Session(2, SecondKey, "42", "User")]);

        Assert.Equal(2, report.ExhaustedFeatures.Count);
        Assert.Empty(report.RestartCandidates);
    }

    [Fact]
    public void RepeatedSessionRecordIsNotAnotherConnectionAndIdsAreScopedToFeature()
    {
        var report = new LicenseIncidentDetector().Evaluate(
            [Feature(FirstKey, 0), Feature(SecondKey, 0)],
            [
                Session(1, FirstKey, "42", "First"),
                Session(1, FirstKey, "42", "First"),
                Session(1, SecondKey, "84", "Second"),
                Session(2, SecondKey, "84", "Second")
            ]);

        var candidate = Assert.Single(report.RestartCandidates);
        Assert.Equal(SecondKey, candidate.Feature.Key);
        Assert.Equal([1L, 2L], Assert.Single(candidate.DuplicateUsers).SessionIds);
    }

    [Fact]
    public void MissingIdFallsBackToTrimmedCaseInsensitiveName()
    {
        var detector = new LicenseIncidentDetector();

        var report = detector.Evaluate(
            [Feature(FirstKey, 0)],
            [
                Session(1, FirstKey, null, "  Тестовый пользователь (11111) "),
                Session(2, FirstKey, null, "тестовый пользователь (22222)")
            ]);

        var duplicate = Assert.Single(Assert.Single(report.RestartCandidates).DuplicateUsers);
        Assert.Equal("name:ТЕСТОВЫЙ ПОЛЬЗОВАТЕЛЬ", duplicate.Identity);
    }

    [Fact]
    public void DuplicateVisibleNameWithDifferentIdsAndSessionSuffixesCreatesCandidate()
    {
        var detector = new LicenseIncidentDetector();

        var report = detector.Evaluate(
            [Feature(FirstKey, 0)],
            [
                Session(1, FirstKey, "10001", "  Тестовый пользователь (11111) "),
                Session(2, FirstKey, "10002", "тестовый пользователь (22222)")
            ]);

        var candidate = Assert.Single(report.RestartCandidates);
        var duplicate = Assert.Single(candidate.DuplicateUsers);
        Assert.Equal("name:ТЕСТОВЫЙ ПОЛЬЗОВАТЕЛЬ", duplicate.Identity);
        Assert.Equal([1L, 2L], duplicate.SessionIds);
    }

    [Fact]
    public void SameSessionsMatchingIdAndNameAreReportedOnce()
    {
        var detector = new LicenseIncidentDetector();

        var report = detector.Evaluate(
            [Feature(FirstKey, 0)],
            [Session(1, FirstKey, "42", "User"), Session(2, FirstKey, "42", "User")]);

        Assert.Single(Assert.Single(report.RestartCandidates).DuplicateUsers);
    }

    [Fact]
    public void MissingIdAndNameNeverCreatesDuplicate()
    {
        var detector = new LicenseIncidentDetector();

        var report = detector.Evaluate(
            [Feature(FirstKey, 0)],
            [Session(1, FirstKey, null, null), Session(2, FirstKey, "", "  ")]);

        Assert.Empty(report.RestartCandidates);
    }

    [Fact]
    public void MultipleExhaustedFeaturesProduceOneReportWithAllCandidates()
    {
        var detector = new LicenseIncidentDetector();

        var report = detector.Evaluate(
            [Feature(FirstKey, 0), Feature(SecondKey, 0)],
            [
                Session(1, FirstKey, "42", "First"),
                Session(2, FirstKey, "42", "First"),
                Session(3, SecondKey, "84", "Second"),
                Session(4, SecondKey, "84", "Second")
            ]);

        Assert.Equal(2, report.RestartCandidates.Count);
        Assert.Equal([FirstKey, SecondKey], report.RestartCandidates.Select(item => item.Feature.Key));
    }

    private static FeatureInfo Feature(
        FeatureKey key,
        int floating,
        bool isRemote = false,
        int remoteMode = 3,
        int maximum = 11) => new(
            key,
            "ДАЛИОН",
            $"Feature {key.FeatureNumber}",
            remoteMode,
            isRemote,
            floating,
            maximum,
            maximum - floating);

    private static SessionInfo Session(
        long sessionId,
        FeatureKey key,
        string? userId,
        string? userName) => new(
            sessionId,
            key,
            userId,
            userName,
            DateTimeOffset.UnixEpoch,
            "rphost",
            100);
}
