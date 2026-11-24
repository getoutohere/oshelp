namespace OSHelp.Core;

public enum TenantRentalFilter { Both, Renting, NotRenting }

public static class TenantDirectory
{
    public static IReadOnlyList<Tenant> Filter(Catalog catalog, Profile profile, IEnumerable<int> sizes,
        TenantRentalFilter rental = TenantRentalFilter.Both, string search = "")
    {
        var accepted = sizes.ToHashSet();
        return catalog.Tenants.Where(tenant =>
        {
            if (!tenant.Sizes.Any(accepted.Contains)) return false;
            bool renting = TenantRentalStatus.Apartments(catalog, profile, tenant).Count > 0;
            if (rental == TenantRentalFilter.Renting && !renting || rental == TenantRentalFilter.NotRenting && renting) return false;
            var text = tenant.Name + " " + string.Join(" ", tenant.Household.Select(h => h.Name)) + " " + tenant.LocationHint + " " + TenantRentalStatus.Label(catalog, profile, tenant);
            return text.Contains(search, StringComparison.CurrentCultureIgnoreCase);
        }).OrderBy(tenant => tenant.Name).ToArray();
    }
}
