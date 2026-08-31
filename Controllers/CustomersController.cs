using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace KappiApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CustomersController : ControllerBase
{
    private readonly AppDbContext _db;

    public CustomersController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> GetCustomers()
    {
        var salonId = int.Parse(User.FindFirst("salonId")!.Value);

        var customers = await _db.Customers
            .Where(c => c.SalonId == salonId)
            .OrderByDescending(c => c.LastVisit)
            .Select(c => new
            {
                c.Id,
                c.Name,
                c.PhoneNumber,
                c.PreferredStylist,
                c.PreferredService,
                c.TotalBookings,
                c.NoShowCount,
                c.LastVisit,
                c.Notes
            })
            .ToListAsync();

        return Ok(customers);
    }

    [HttpPatch("{id}/notes")]
    public async Task<IActionResult> UpdateNotes(int id, [FromBody] UpdateNotesRequest request)
    {
        var salonId = int.Parse(User.FindFirst("salonId")!.Value);
        var customer = await _db.Customers
            .FirstOrDefaultAsync(c => c.Id == id && c.SalonId == salonId);

        if (customer == null)
            return NotFound();

        customer.Notes = request.Notes;
        await _db.SaveChangesAsync();

        return Ok(new { customer.Id, customer.Notes });
    }
}

public class UpdateNotesRequest
{
    public string? Notes { get; set; }
}