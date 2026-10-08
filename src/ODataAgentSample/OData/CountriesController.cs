using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Deltas;
using Microsoft.AspNetCore.OData.Formatter;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Results;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using Microsoft.EntityFrameworkCore;
using ODataAgentSample.Domain;
using ODataAgentSample.Infrastructure.Db;
using ODataAgentSample.Security;

namespace ODataAgentSample.OData;

public class CountriesController(AppDbContext db) : ODataController
{
    [EnableQuery(PageSize = 100, MaxTop = 250, MaxExpansionDepth = 2)]
    public IQueryable<Country> Get() => db.Countries.AsNoTracking();

    [EnableQuery(MaxExpansionDepth = 2)]
    public SingleResult<Country> Get([FromODataUri] int key) =>
        SingleResult.Create(db.Countries.AsNoTracking().Where(c => c.Id == key));

    [Authorize(Policy = Policies.Writer)]
    public async Task<IActionResult> Post([FromBody] Country entity, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        db.Countries.Add(entity);
        await db.SaveChangesAsync(ct);
        return Created(entity);
    }

    [Authorize(Policy = Policies.Writer)]
    public async Task<IActionResult> Patch([FromODataUri] int key, [FromBody] Delta<Country> delta, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var existing = await db.Countries.FirstOrDefaultAsync(c => c.Id == key, ct);
        if (existing is null)
            return NotFound();

        delta.Patch(existing);
        await db.SaveChangesAsync(ct);
        return Updated(existing);
    }

    [Authorize(Policy = Policies.Writer)]
    public async Task<IActionResult> Delete([FromODataUri] int key, CancellationToken ct)
    {
        var existing = await db.Countries.FirstOrDefaultAsync(c => c.Id == key, ct);
        if (existing is null)
            return NotFound();

        db.Countries.Remove(existing);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
