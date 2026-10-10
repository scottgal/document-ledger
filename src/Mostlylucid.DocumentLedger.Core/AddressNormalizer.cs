using System.Text;
using System.Text.RegularExpressions;

namespace Mostlylucid.DocumentLedger.Core;

public static partial class AddressNormalizer
{
    public const string Version = "uk-address-components-v1";
    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
    [GeneratedRegex(@"\b(GIR\s*0AA|[A-Z]{1,2}\d[A-Z\d]?\s*\d[A-Z]{2})\b", RegexOptions.IgnoreCase)]
    private static partial Regex PostcodePattern();
    [GeneratedRegex(@"^(?:FLAT|APARTMENT|APT|UNIT|SUITE)\s+([\w/-]+)\s*", RegexOptions.IgnoreCase)]
    private static partial Regex UnitPattern();
    [GeneratedRegex(@"^(\d+[A-Z]?(?:[-/]\d+[A-Z]?)?)\s+(.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex StreetPattern();

    public static string Text(string value) => Spaces().Replace(value.Normalize(NormalizationForm.FormKC)
        .ToUpperInvariant().Trim().Trim(',', '.', ';'), " ");

    public static AddressParts Normalize(AddressParts parts)
    {
        var values = parts.Values().ToDictionary(p => p.Key, p => Text(p.Value));
        values["postcode"] = values["postcode"].Replace(" ", "");
        values["country"] = values["country"] switch
        {
            "UK" or "U.K" or "GB" or "GBR" or "UNITED KINGDOM" => "GB",
            var country => country
        };
        // Only a final street suffix: never turn 'St Andrews Road' into 'Street Andrews Road'.
        var words = values["street"].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length > 1)
        {
            words[^1] = words[^1].TrimEnd('.') switch
            {
                "ST" => "STREET", "RD" => "ROAD", "AVE" => "AVENUE",
                "LN" => "LANE", "DR" => "DRIVE", var suffix => suffix
            };
            values["street"] = string.Join(' ', words);
        }
        return AddressParts.From(values);
    }

    // Conservative UK-style baseline. Unrecognised content remains explicit rather than disappearing.
    public static AddressParts Parse(string value, string component = "")
    {
        if (component.Length > 0)
        {
            if (!new AddressParts().Values().ContainsKey(component) || component == "unparsed")
                throw new ArgumentException("Unknown address component.", nameof(component));
            if (component == "street")
            {
                var street = StreetPattern().Match(Text(value));
                if (street.Success) return new() { Building = street.Groups[1].Value, Street = street.Groups[2].Value };
            }
            return AddressParts.From(new Dictionary<string, string> { [component] = value });
        }
        var remaining = value;
        var postcode = PostcodePattern().Match(remaining);
        var parts = new AddressParts { Postcode = postcode.Success ? postcode.Value : "" };
        if (postcode.Success) remaining = remaining.Remove(postcode.Index, postcode.Length);
        var chunks = remaining.Split([',', '\r', '\n', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var locality = new List<string>();
        var unparsed = new List<string>();
        foreach (var chunk in chunks)
        {
            var text = Text(chunk);
            if (text.Length == 0) continue;
            if (text is "UK" or "GB" or "GBR" or "UNITED KINGDOM") { parts = parts with { Country = text }; continue; }
            var unit = UnitPattern().Match(text);
            if (unit.Success)
            {
                if (parts.Unit.Length > 0 && parts.Unit != unit.Groups[1].Value)
                {
                    unparsed.Add(text); // Retain a conflicting second unit instead of silently overwriting the first.
                    continue;
                }
                parts = parts with { Unit = unit.Groups[1].Value };
                text = text[unit.Length..].Trim();
                if (text.Length == 0) continue;
            }
            var street = StreetPattern().Match(text);
            if (street.Success && parts.Street.Length == 0)
                parts = parts with { Building = street.Groups[1].Value, Street = street.Groups[2].Value };
            else if (parts.Street.Length > 0) locality.Add(text);
            else unparsed.Add(text);
        }
        return parts with { Locality = string.Join(", ", locality), Unparsed = string.Join(", ", unparsed) };
    }
}

public static class FieldRoleAlignment
{
    public static string AddressRole(string label, string section = "")
    {
        var name = AddressNormalizer.Text(label).TrimEnd(':');
        var context = AddressNormalizer.Text(section);
        if (name.Contains("CORRESPONDENCE") || name.Contains("SEND LETTERS") || name.Contains("MAILING ADDRESS"))
            return "correspondence.address";
        if (name.Contains("CURRENT ADDRESS") || name.Contains("APPLICANT ADDRESS")) return "applicant.current_address";
        if (name is "PROPERTY" or "PROPERTY ADDRESS" or "SUBJECT PROPERTY" or "ADDRESS OF PROPERTY") return "property.address";
        if (!name.Contains("ADDRESS") && name is not ("STREET" or "TOWN" or "LOCALITY" or "POSTCODE" or "UNIT" or "BUILDING" or "COUNTRY")) return "";
        if (context.Contains("CORRESPONDENCE")) return "correspondence.address";
        if (context.Contains("APPLICANT") || context.Contains("CONTACT")) return "applicant.current_address";
        if (context.Contains("PROPERTY") || context.Contains("VALUATION SUBJECT")) return "property.address";
        return "";
    }
}
