using System.Xml.Linq;
using CodeAssessment.Shared;

namespace CodeAssessment.Tests.Internal;

internal static class TrxParser
{
    private static readonly XNamespace Ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";

    public static (int total, int passed, int failed, List<(string name, string outcome, string message)> tests)
        Parse(string trxPath)
    {
        if (!File.Exists(trxPath))
            return (0, 0, 0, new());

        var doc = XDocument.Load(trxPath);
        int total = 0, passed = 0, failed = 0;
        var list = new List<(string name, string outcome, string message)>();

        foreach (var r in doc.Descendants(Ns + "UnitTestResult"))
        {
            total++;

            var outcome = (string?)r.Attribute("outcome") ?? "";
            var name    = (string?)r.Attribute("testName") ?? "";
            var msg     = r.Descendants(Ns + "Message").FirstOrDefault()?.Value?.Trim() ?? "";

            if (outcome.Equals("Passed", StringComparison.OrdinalIgnoreCase))
                passed++;
            else
                failed++;

            list.Add((name, outcome, msg));
        }

        return (total, passed, failed, list);
    }
}
