using System.Xml.Linq;
using DnRelay.Models;

namespace DnRelay.Parsing;

static class TrxParser
{
    public static TrxSummary? TryParse(string trxPath)
    {
        try
        {
            if (!File.Exists(trxPath))
            {
                return null;
            }

            var document = XDocument.Load(trxPath);
            var ns = document.Root?.Name.Namespace ?? XNamespace.None;

            var counters = document.Descendants(ns + "Counters").FirstOrDefault();
            var unitTestResults = document.Descendants(ns + "UnitTestResult").ToList();

            var total = ParseIntAttribute(counters, "total");
            var passed = ParseIntAttribute(counters, "passed");
            var failed = ParseIntAttribute(counters, "failed");
            var error = ParseIntAttribute(counters, "error");
            var timeout = ParseIntAttribute(counters, "timeout");
            var aborted = ParseIntAttribute(counters, "aborted");
            var inconclusive = ParseIntAttribute(counters, "inconclusive");
            var notExecuted = ParseIntAttribute(counters, "notExecuted");
            var skippedFromResults = unitTestResults.Count(result => IsSkippedOutcome((string?)result.Attribute("outcome")));
            var skipped = Math.Max(inconclusive + notExecuted, skippedFromResults);
            var failedOverall = failed + error + timeout + aborted;

            var unitTests = document
                .Descendants(ns + "UnitTest")
                .ToDictionary(
                    test => (string?)test.Attribute("id") ?? string.Empty,
                    test => (string?)test.Attribute("name") ?? string.Empty,
                    StringComparer.Ordinal);

            var failedTests = unitTestResults
                .Where(result => IsFailedOutcome((string?)result.Attribute("outcome")))
                .Select(result =>
                {
                    var id = (string?)result.Attribute("testId") ?? string.Empty;
                    var name = (string?)result.Attribute("testName");
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        return name.Trim();
                    }

                    return unitTests.TryGetValue(id, out var unitTestName) && !string.IsNullOrWhiteSpace(unitTestName)
                        ? unitTestName.Trim()
                        : id;
                })
                .Where(static name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            return new TrxSummary(total, passed, failedOverall, skipped, failedTests);
        }
        catch
        {
            return null;
        }
    }

    private static int ParseIntAttribute(XElement? element, string name)
        => int.TryParse((string?)element?.Attribute(name), out var value) ? value : 0;

    private static bool IsFailedOutcome(string? outcome)
        => string.Equals(outcome, "Failed", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(outcome, "Error", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(outcome, "Timeout", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(outcome, "Aborted", StringComparison.OrdinalIgnoreCase);

    private static bool IsSkippedOutcome(string? outcome)
        => string.Equals(outcome, "NotExecuted", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(outcome, "Inconclusive", StringComparison.OrdinalIgnoreCase);
}
