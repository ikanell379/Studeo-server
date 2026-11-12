using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Studeo.Api.Models;
using Studeo.Api.Repositories;
using Studeo.Api.Services;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Studeo")
    ?? throw new InvalidOperationException("Connection string 'Studeo' is missing (set it in appsettings.Development.json).");

builder.Services.AddDbContext<StudeoDbContext>(o => o.UseNpgsql(connectionString));

builder.Services.AddControllers();
builder.Services.AddValidatorsFromAssemblyContaining<Program>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Repositories
builder.Services.AddScoped<IHealthRepository, HealthRepository>();

// Services
builder.Services.AddScoped<IHealthService, HealthService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();

    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<StudeoDbContext>().Database.Migrate();
}
else
{
    app.UseHttpsRedirection();
}

app.MapControllers();

app.Run();

public partial class Program;
