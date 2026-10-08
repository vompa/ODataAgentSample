using System.Linq.Expressions;
using Microsoft.AspNetCore.OData.Query.Expressions;
using Microsoft.OData.UriParser;
using ODataAgentSample.Domain;

namespace ODataAgentSample.OData;

/// <summary>Eigenes $search fuer Countries, siehe <see cref="WorldRegionAliases"/>.</summary>
public class CountrySearchBinder : QueryBinder, ISearchBinder
{
    public Expression BindSearch(SearchClause searchClause, QueryBinderContext context)
    {
        ArgumentNullException.ThrowIfNull(searchClause);
        ArgumentNullException.ThrowIfNull(context);

        if (searchClause.Expression is SearchTermNode node)
        {
            if (!WorldRegionAliases.ByTerm.TryGetValue(node.Text, out var regionId))
                throw new InvalidOperationException(
                    $"Unbekannter Suchbegriff '{node.Text}'. Erlaubt: {string.Join(", ", WorldRegionAliases.ByTerm.Keys)}");

            Expression<Func<Country, bool>> predicate = c => c.WorldRegionId == regionId;
            return predicate;
        }

        var body = BindSingleValueNode(searchClause.Expression, context);
        return Expression.Lambda(body, context.CurrentParameter);
    }
}
