using GccLicenseWatchdog.Guardant;
using System.Text.RegularExpressions;

namespace GccLicenseWatchdog.Detection;

public sealed record DuplicateUser(
    string Identity,
    string? DisplayName,
    IReadOnlyList<long> SessionIds);

public sealed record RestartCandidate(
    FeatureInfo Feature,
    int SessionCount,
    int UniqueUserCount,
    IReadOnlyList<DuplicateUser> DuplicateUsers);

public sealed record DetectionReport(
    IReadOnlyList<FeatureInfo> ExhaustedFeatures,
    IReadOnlyList<RestartCandidate> RestartCandidates);

public sealed class LicenseIncidentDetector
{
    private static readonly Regex NumericSessionSuffix = new(
        @"\s*\(\d+\)\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public DetectionReport Evaluate(
        IReadOnlyList<FeatureInfo> features,
        IReadOnlyList<SessionInfo> sessions)
    {
        var exhausted = features
            .Where(IsExhaustedLocalNetworkFeature)
            .ToArray();
        var candidates = new List<RestartCandidate>();

        foreach (var feature in exhausted)
        {
            var featureSessions = sessions
                .Where(session => session.FeatureKey == feature.Key)
                .ToArray();
            var identified = featureSessions
                .Select(session => new IdentifiedSession(
                    session,
                    GetIdIdentity(session),
                    GetNameIdentity(session)))
                .ToArray();
            var duplicates = FindDuplicates(identified);

            if (duplicates.Count == 0)
            {
                continue;
            }

            candidates.Add(new RestartCandidate(
                feature,
                featureSessions.Length,
                CountConnectedUsers(identified),
                duplicates));
        }

        return new DetectionReport(exhausted, candidates);
    }

    private static bool IsExhaustedLocalNetworkFeature(FeatureInfo feature) =>
        feature.RemoteMode == 3 &&
        !feature.IsRemote &&
        feature.MaxConcurrentResource > 0 &&
        feature.FloatingResource == 0;

    private static IReadOnlyList<DuplicateUser> FindDuplicates(IReadOnlyList<IdentifiedSession> sessions)
    {
        var duplicates = new List<DuplicateUser>();
        var reportedSessionSets = new HashSet<string>(StringComparer.Ordinal);
        AddDuplicateGroups(sessions, item => item.IdIdentity, duplicates, reportedSessionSets);
        AddDuplicateGroups(sessions, item => item.NameIdentity, duplicates, reportedSessionSets);
        return duplicates;
    }

    private static void AddDuplicateGroups(
        IReadOnlyList<IdentifiedSession> sessions,
        Func<IdentifiedSession, string?> identitySelector,
        ICollection<DuplicateUser> duplicates,
        ISet<string> reportedSessionSets)
    {
        foreach (var group in sessions
            .Where(item => identitySelector(item) is not null)
            .GroupBy(item => identitySelector(item)!, StringComparer.Ordinal)
            .Where(group => group.Count() >= 2))
        {
            var sessionIds = group
                .Select(item => item.Session.SessionId)
                .OrderBy(id => id)
                .ToArray();
            var sessionSet = string.Join(',', sessionIds);
            if (!reportedSessionSets.Add(sessionSet))
            {
                continue;
            }

            duplicates.Add(new DuplicateUser(
                group.Key,
                group.Select(item => item.Session.UserName)
                    .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)),
                sessionIds));
        }
    }

    private static int CountConnectedUsers(IReadOnlyList<IdentifiedSession> sessions)
    {
        var parents = Enumerable.Range(0, sessions.Count).ToArray();
        var firstSessionByIdentity = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < sessions.Count; index++)
        {
            UnionByIdentity(sessions[index].IdIdentity, index, parents, firstSessionByIdentity);
            UnionByIdentity(sessions[index].NameIdentity, index, parents, firstSessionByIdentity);
        }

        return Enumerable.Range(0, sessions.Count)
            .Select(index => FindRoot(index, parents))
            .Distinct()
            .Count();
    }

    private static void UnionByIdentity(
        string? identity,
        int index,
        int[] parents,
        IDictionary<string, int> firstSessionByIdentity)
    {
        if (identity is null)
        {
            return;
        }

        if (firstSessionByIdentity.TryGetValue(identity, out var firstIndex))
        {
            var indexRoot = FindRoot(index, parents);
            var firstRoot = FindRoot(firstIndex, parents);
            parents[indexRoot] = firstRoot;
        }
        else
        {
            firstSessionByIdentity.Add(identity, index);
        }
    }

    private static int FindRoot(int index, int[] parents)
    {
        while (parents[index] != index)
        {
            parents[index] = parents[parents[index]];
            index = parents[index];
        }

        return index;
    }

    private static string? GetIdIdentity(SessionInfo session) =>
        string.IsNullOrWhiteSpace(session.UserId)
            ? null
            : $"id:{session.UserId.Trim()}";

    private static string? GetNameIdentity(SessionInfo session)
    {
        if (string.IsNullOrWhiteSpace(session.UserName))
        {
            return null;
        }

        var name = NumericSessionSuffix.Replace(session.UserName, string.Empty).Trim();
        return name.Length == 0 ? null : $"name:{name.ToUpperInvariant()}";
    }

    private sealed record IdentifiedSession(
        SessionInfo Session,
        string? IdIdentity,
        string? NameIdentity);
}
