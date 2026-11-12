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

builder.Services.AddSingleton(TimeProvider.System);

// Repositories
builder.Services.AddScoped<IHealthRepository, HealthRepository>();
builder.Services.AddScoped<IDocumentRepository, DocumentRepository>();
builder.Services.AddScoped<IStudySetRepository, StudySetRepository>();

// Services
builder.Services.AddScoped<IHealthService, HealthService>();
builder.Services.AddSingleton<IPdfTextExtractor, PdfTextExtractor>();
builder.Services.AddSingleton<ISectioner, Sectioner>();
builder.Services.AddScoped<IDocumentService, DocumentService>();
builder.Services.AddAiClient(builder.Configuration); // "Ai:Provider": Claude | Gemini
builder.Services.AddScoped<IStudySetService, StudySetService>();

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
