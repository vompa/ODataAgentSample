using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Results;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using Microsoft.EntityFrameworkCore;
using ODataAgentSample.Domain;
using ODataAgentSample.Infrastructure.Db;

namespace ODataAgentSample.OData;

// Gegenueber dem Original (volles CRUD) sind die Stammdaten-Regionen hier bewusst nur lesbar.

public class WorldRegionsController(AppDbContext db) : ODataController
{
    [EnableQuery(PageSize = 100, MaxExpansionDepth = 2)]
    public IQueryable<WorldRegion> Get() => db.WorldRegions.AsNoTracking();

    [EnableQuery(MaxExpansionDepth = 2)]
    public SingleResult<WorldRegion> Get([FromODataUri] int key) =>
        SingleResult.Create(db.WorldRegions.AsNoTracking().Where(r => r.Id == key));
}

public class CountryRegionsController(AppDbContext db) : ODataController
{
    [EnableQuery(PageSize = 100, MaxExpansionDepth = 2)]
    public IQueryable<CountryRegion> Get() => db.CountryRegions.AsNoTracking();

    [EnableQuery(MaxExpansionDepth = 2)]
    public SingleResult<CountryRegion> Get([FromODataUri] int key) =>
        SingleResult.Create(db.CountryRegions.AsNoTracking().Where(r => r.Id == key));
}
