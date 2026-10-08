using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;
using ODataAgentSample.Domain;

namespace ODataAgentSample.OData;

public static class CountriesEdmModel
{
    public static IEdmModel Build()
    {
        var builder = new ODataConventionModelBuilder
        {
            Namespace = "ODataAgentSample",
            ContainerName = "CountriesContext",
            BindingOptions = NavigationPropertyBindingOption.Auto,
        };

        builder.EntitySet<WorldRegion>("WorldRegions").EntityType.HasKey(x => x.Id);
        builder.EntitySet<CountryRegion>("CountryRegions").EntityType.HasKey(x => x.Id);
        builder.EntitySet<Country>("Countries").EntityType.HasKey(x => x.Id);

        return builder.GetEdmModel();
    }
}
