using OSHelp.Core;

internal static class TenantDirectoryChecks
{
    public static void Run(Catalog catalog, Action<bool,string> check)
    {
        var profile = Profile.Create(catalog,"Tenant filters");
        foreach (var apartment in profile.Apartments) apartment.Occupant = "";
        var jonne = catalog.Tenants.Single(t => t.Name == "Jonne Reis");
        var fenella = catalog.Tenants.Single(t => t.Name == "Fenella Hunt");
        profile.Apartment(4).Occupant = jonne.Id;
        var furniture = profile.Apartments.First(a => a.Number is not (1 or 2 or 4));
        furniture.Occupant = fenella.Id; furniture.OnlyFurniture = true;
        bool Includes(Tenant tenant,int size) => TenantDirectory.Filter(catalog,profile,[size]).Any(t => t.Id == tenant.Id);
        check(Includes(jonne,0) && Includes(jonne,1) && !Includes(jonne,2),"small/medium resident matches both individual accepted-size filters");
        check(!Includes(fenella,0) && Includes(fenella,1) && Includes(fenella,2),"medium/large resident matches both individual accepted-size filters");
        check(TenantDirectory.Filter(catalog,profile,[0,1,2]).Count == 31,"all three size options include all actual resident definitions");
        check(TenantDirectory.Filter(catalog,profile,[]).Count == 0,"no enabled size options produce an empty list");
        check(TenantDirectory.Filter(catalog,profile,[0,1]).Count(t => t.Id == jonne.Id) == 1,"multi-size residents appear once when several accepted sizes are enabled");
        var renting = TenantDirectory.Filter(catalog,profile,[0,1,2],TenantRentalFilter.Renting);
        check(renting.Count == 1 && renting[0].Id == jonne.Id,"renting uses active profile rental assignments");
        var notRenting = TenantDirectory.Filter(catalog,profile,[0,1,2],TenantRentalFilter.NotRenting);
        check(notRenting.All(t => t.Id != jonne.Id) && notRenting.Any(t => t.Id == fenella.Id),"not renting excludes actual renter but includes furniture-only placement");
        check(renting.Concat(notRenting).Select(t => t.Id).Distinct().Count() == 31,"both rental states partition the list without omitted or duplicate tenants");
        check(TenantDirectory.Filter(catalog,profile,[2],TenantRentalFilter.Renting).Count == 0,"size and rental filters intersect");
        check(TenantDirectory.Filter(catalog,profile,[0],TenantRentalFilter.Renting,"JONNE").Single().Id == jonne.Id,"search composes with size and rental filters, ignoring case");
        check(TenantDirectory.Filter(catalog,profile,[0],TenantRentalFilter.Renting,"Deekula A").Count == 0,"location search remains an additional condition");
        profile.Apartment(4).OnlyFurniture = true;
        check(TenantDirectory.Filter(catalog,profile,[0,1,2],TenantRentalFilter.Renting).Count == 0,"rental filters update after furniture-only state changes");
        profile.Apartment(1).Occupant = jonne.Id; profile.Apartment(2).Occupant = jonne.Id;
        check(TenantDirectory.Filter(catalog,profile,[0,1,2],TenantRentalFilter.Renting).Count == 0,"shop and player apartment never count as rentals");
        var manual = Profile.Create(catalog,"Unrecorded occupancy");
        check(TenantDirectory.Filter(catalog,manual,[0,1,2],TenantRentalFilter.Renting).Count == 0 && TenantDirectory.Filter(catalog,manual,[0,1,2],TenantRentalFilter.NotRenting).Count == 31,"unrecorded occupancy never invents an active renter");
        foreach (var mask in Enumerable.Range(0,8))
        {
            var sizes = Enumerable.Range(0,3).Where(s => (mask & 1 << s) != 0).ToArray();
            var both = TenantDirectory.Filter(catalog,profile,sizes);
            var actual = both.Select(t => t.Id).ToHashSet();
            var expected = catalog.Tenants.Where(t => t.Sizes.Intersect(sizes).Any()).Select(t => t.Id).ToHashSet();
            check(actual.SetEquals(expected),"every size combination uses any accepted size, mask "+mask);
        }
        Console.WriteLine("Tenant filters: actual multi-size residents / current occupancy / furniture-only and non-rental exclusions / search composition / all size combinations passed.");
    }
}
