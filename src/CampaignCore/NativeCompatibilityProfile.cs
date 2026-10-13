using System.Text.RegularExpressions;
namespace CampaignCore;
public static class NativeSymbolNames
{
    // Compiler-generated ordinal changes do not change the source method identity.
    // Callers still require exactly one match; overloads/ambiguous types fail closed.
    public static string Stable(string value) => Regex.Replace(Regex.Replace(value.Replace('/','.'), @"(?<=d__)\d+|(?<=DisplayClass)\d+_\d+", "#"), @"(?<=\|)\d+_\d+", "#");
}
