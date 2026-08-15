namespace WorkdayJobManager;

internal static class LocationFacetOrganizer
{
    private const string OtherLocations = "Other locations";

    private static readonly IReadOnlyDictionary<string, string> UnitedStatesNames =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["AL"] = "Alabama", ["AK"] = "Alaska", ["AZ"] = "Arizona",
            ["AR"] = "Arkansas", ["CA"] = "California", ["CO"] = "Colorado",
            ["CT"] = "Connecticut", ["DE"] = "Delaware", ["DC"] = "District of Columbia",
            ["FL"] = "Florida", ["GA"] = "Georgia", ["HI"] = "Hawaii",
            ["ID"] = "Idaho", ["IL"] = "Illinois", ["IN"] = "Indiana",
            ["IA"] = "Iowa", ["KS"] = "Kansas", ["KY"] = "Kentucky",
            ["LA"] = "Louisiana", ["ME"] = "Maine", ["MD"] = "Maryland",
            ["MA"] = "Massachusetts", ["MI"] = "Michigan", ["MN"] = "Minnesota",
            ["MS"] = "Mississippi", ["MO"] = "Missouri", ["MT"] = "Montana",
            ["NE"] = "Nebraska", ["NV"] = "Nevada", ["NH"] = "New Hampshire",
            ["NJ"] = "New Jersey", ["NM"] = "New Mexico", ["NY"] = "New York",
            ["NC"] = "North Carolina", ["ND"] = "North Dakota", ["OH"] = "Ohio",
            ["OK"] = "Oklahoma", ["OR"] = "Oregon", ["PA"] = "Pennsylvania",
            ["RI"] = "Rhode Island", ["SC"] = "South Carolina", ["SD"] = "South Dakota",
            ["TN"] = "Tennessee", ["TX"] = "Texas", ["UT"] = "Utah",
            ["VT"] = "Vermont", ["VA"] = "Virginia", ["WA"] = "Washington",
            ["WV"] = "West Virginia", ["WI"] = "Wisconsin", ["WY"] = "Wyoming"
        };

    public static LocationFacetOrganization Organize(
        CompanyDefinition company,
        string? countryId,
        IReadOnlyList<FacetOption> availableLocations)
    {
        var remote = availableLocations
            .Where(location => company.IsRemoteLocation(location.Id))
            .OrderBy(location => location.Label, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var physical = availableLocations
            .Where(location => !company.IsRemoteLocation(location.Id))
            .ToArray();

        var grouped = new Dictionary<string, List<FacetOption>>(StringComparer.Ordinal);
        var fallback = new List<FacetOption>();
        foreach (var location in physical)
        {
            if (TryParseUnitedStatesLocation(location.Label, out var locationName, out var stateCode))
            {
                if (!grouped.TryGetValue(stateCode, out var stateLocations))
                {
                    stateLocations = [];
                    grouped[stateCode] = stateLocations;
                }
                stateLocations.Add(location with { DisplayLabel = locationName });
            }
            else
            {
                fallback.Add(location with { DisplayLabel = location.Label });
            }
        }

        var groupsResult = grouped
            .Select(pair => new LocationFacetGroup(
                pair.Key,
                UnitedStatesNames[pair.Key],
                pair.Value.OrderBy(location => location.DisplayLabel, StringComparer.OrdinalIgnoreCase).ToArray()))
            .OrderBy(group => group.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (fallback.Count > 0)
        {
            groupsResult.Add(new LocationFacetGroup(
                "other",
                OtherLocations,
                fallback.OrderBy(location => location.Label, StringComparer.OrdinalIgnoreCase).ToArray()));
        }

        return new LocationFacetOrganization(
            groupsResult.SelectMany(group => group.Locations).ToArray(),
            remote,
            groupsResult,
            grouped.Sum(pair => pair.Value.Count),
            fallback.Select(location => location.Label).ToArray());
    }

    private static bool TryParseUnitedStatesLocation(
        string label,
        out string locationName,
        out string stateCode)
    {
        locationName = "";
        stateCode = "";
        var comma = label.LastIndexOf(',');
        if (comma > 0 && comma < label.Length - 1)
        {
            var candidateName = label[..comma].Trim();
            var candidateState = label[(comma + 1)..].Trim();
            if (candidateName.Length > 0 && UnitedStatesNames.ContainsKey(candidateState))
            {
                locationName = candidateName;
                stateCode = candidateState;
                return true;
            }
        }

        var dash = label.IndexOf(" - ", StringComparison.Ordinal);
        if (dash == 2)
        {
            var candidateState = label[..dash].Trim();
            var candidateName = label[(dash + 3)..].Trim();
            if (candidateName.Length > 0 && UnitedStatesNames.ContainsKey(candidateState))
            {
                locationName = candidateName;
                stateCode = candidateState;
                return true;
            }
        }

        return false;
    }
}

internal sealed record LocationFacetOrganization(
    IReadOnlyList<FacetOption> PhysicalLocations,
    IReadOnlyList<FacetOption> RemoteLocations,
    IReadOnlyList<LocationFacetGroup> Groups,
    int StateMappedLocationCount,
    IReadOnlyList<string> UnmappedLocationLabels);
