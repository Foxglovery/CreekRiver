using CreekRiver.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Json;
using System.Reflection.PortableExecutable;
using CreekRiver.Models.DTOs;



var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// allows passing datetimes without time zone data 
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

// allows our api endpoints to access the database through Entity Framework Core
builder.Services.AddNpgsql<CreekRiverDbContext>(builder.Configuration["CreekRiverDbConnectionString"]);


var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.MapGet("/api/campsites", (CreekRiverDbContext db) =>
{
    return db.Campsites
    .Select(c => new CampsiteDTO
    {
        Id = c.Id,
        Nickname = c.Nickname,
        ImageUrl = c.ImageUrl,
        CampsiteTypeId = c.CampsiteTypeId
    }).ToList();
});

app.MapGet("/api/campsites/{id}", (CreekRiverDbContext db, int id) =>
{
    //to handle error change to declare a var campsite
    var campsite = db.Campsites
        .Include(c => c.CampsiteType)
        .Select(c => new CampsiteDTO
        {
            Id = c.Id,
            Nickname = c.Nickname,
            CampsiteTypeId = c.CampsiteTypeId,
            CampsiteType = new CampsiteTypeDTO
            {
                Id = c.CampsiteType.Id,
                CampsiteTypeName = c.CampsiteType.CampsiteTypeName,
                FeePerNight = c.CampsiteType.FeePerNight,
                MaxReservationDays = c.CampsiteType.MaxReservationDays
            }
        })
        .SingleOrDefault(c => c.Id == id);

    if (campsite == null)
    {
        return Results.NotFound("Something appears to have gone wrong. Try again");
    }
    return Results.Ok(campsite);
});

app.MapPost("/api/campsites", (CreekRiverDbContext db, Campsite campsite) =>
{
    db.Campsites.Add(campsite);
    db.SaveChanges();
    return Results.Created($"/api/campsites/{campsite.Id}", campsite);
});

app.MapDelete("/api/campsites/{id}", (CreekRiverDbContext db, int id) =>
{
    Campsite campsite = db.Campsites.SingleOrDefault(campsite => campsite.Id == id);
    if (campsite == null)
    {
        return Results.NotFound();
    }
    db.Campsites.Remove(campsite);
    db.SaveChanges();
    return Results.NoContent();

});

app.MapPut("/api/campsites/{id}", (CreekRiverDbContext db, int id, Campsite campsite) =>
{
    Campsite campsiteToUpdate = db.Campsites.SingleOrDefault(campsite => campsite.Id == id);
    if (campsiteToUpdate == null)
    {
        return Results.NotFound();
    }
    //then just update each field
    campsiteToUpdate.Nickname = campsite.Nickname;
    campsiteToUpdate.CampsiteTypeId = campsite.CampsiteTypeId;
    campsiteToUpdate.ImageUrl = campsite.ImageUrl;

    db.SaveChanges();
    return Results.NoContent();
});

app.MapGet("/api/reservations", (CreekRiverDbContext db) =>
{
    return db.Reservations
        .Include(r => r.UserProfile)
        .Include(r => r.Campsite)
        //uses then include to join types to campsite, not types to reservation
        .ThenInclude(c => c.CampsiteType)
        .OrderBy(res => res.CheckinDate)
        .Select(r => new ReservationDTO
        {
            Id = r.Id,
            CampsiteId = r.CampsiteId,
            UserProfileId = r.UserProfileId,
            CheckinDate = r.CheckinDate,
            CheckoutDate = r.CheckoutDate,
            UserProfile = new UserProfileDTO
            {
                Id = r.UserProfile.Id,
                FirstName = r.UserProfile.FirstName,
                LastName = r.UserProfile.LastName,
                Email = r.UserProfile.Email
            },
            Campsite = new CampsiteDTO
            {
                Id = r.Campsite.Id,
                Nickname = r.Campsite.Nickname,
                ImageUrl = r.Campsite.ImageUrl,
                CampsiteTypeId = r.Campsite.CampsiteTypeId,
                CampsiteType = new CampsiteTypeDTO
                {
                    Id = r.Campsite.CampsiteType.Id,
                    CampsiteTypeName = r.Campsite.CampsiteType.CampsiteTypeName,
                    MaxReservationDays = r.Campsite.CampsiteType.MaxReservationDays,
                    FeePerNight = r.Campsite.CampsiteType.FeePerNight
                }
            }
        })
        .ToList();
});

app.MapPost("/api/reservations", (CreekRiverDbContext db, Reservation newRes) =>
{
    DateTime checkinDateToCheck = newRes.CheckinDate;
    DateTime checkoutDateToCheck = newRes.CheckoutDate;
    bool isDateReserved = db.Reservations.Any(r => r.CheckinDate == checkinDateToCheck);
    //to validate datetimes set time for today
    DateTime today = DateTime.Today;
    //then just check against today. easy peasy
    if (checkinDateToCheck < today)
    {
        return Results.BadRequest("Check in cannot be in the past.");
    }
    else if (checkinDateToCheck == today)
    {
        return Results.BadRequest("Same day check in is forbidden.");
    }
    if (newRes.CheckoutDate == DateTime.MinValue)
    {
        return Results.BadRequest("Check out date is required.");
    }
    if (newRes.CheckoutDate <= newRes.CheckinDate)
    {
        return Results.BadRequest("Check out cannot predate check in.");
    }
    

    if (isDateReserved)
    {
        return Results.BadRequest("Campsite is not available for these dates");
    }
    
    Campsite campsite = db.Campsites.Include(c => c.CampsiteType).Single(c => c.Id == newRes.CampsiteId);
    if (campsite !=null && newRes.TotalNights > campsite.CampsiteType.MaxReservationDays)
    {
        return Results.BadRequest("Reservation exceeds maximum reservation time for this site");
    }

    if (newRes.CheckoutDate <= newRes.CheckinDate)
    {
        return Results.BadRequest("Reservation checkout must be at least one day before check-in");
    }
   try
   {
    db.Reservations.Add(newRes);
    db.SaveChanges();
    return Results.Created($"/api/reservations/{newRes.Id}", newRes);
   }
   catch (DbUpdateException)
   {
    return Results.BadRequest("Invalid data submitted");
   } 
});

app.MapDelete("/api/reservations/{id}", (CreekRiverDbContext db, int id) =>
{
    Reservation reservation = db.Reservations.SingleOrDefault(reservation => reservation.Id == id);
    if (reservation == null)
    {
        return Results.NotFound();
    }
    db.Reservations.Remove(reservation);
    db.SaveChanges();
    return Results.NoContent();
});

app.Run();

