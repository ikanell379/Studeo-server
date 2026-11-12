using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Studeo.Api.Auth;
using Studeo.Api.Models;
using Studeo.Api.Repositories;
using Studeo.Api.Services;
using Studeo.Api.Services.Export;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Studeo")
    ?? throw new InvalidOperationException("Connection string 'Studeo' is missing (set it in appsettings.Development.json).");

builder.Services.AddDbContext<StudeoDbContext>(o => o.UseNpgsql(connectionString));

builder.Services.AddKeycloakAuth(builder.Configuration, builder.Environment.IsDevelopment());
builder.Services.AddControllers();
builder.Services.AddValidatorsFromAssemblyContaining<Program>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddSingleton(TimeProvider.System);

// Repositories
builder.Services.AddScoped<IHealthRepository, HealthRepository>();
builder.Services.AddScoped<IDocumentRepository, DocumentRepository>();
builder.Services.AddScoped<IStudySetRepository, StudySetRepository>();
builder.Services.AddScoped<ICourseRepository, CourseRepository>();

// Services
builder.Services.AddScoped<IHealthService, HealthService>();
builder.Services.AddSingleton<IPdfTextExtractor, PdfTextExtractor>();
builder.Services.AddSingleton<ISectioner, Sectioner>();
builder.Services.AddScoped<IDocumentService, DocumentService>();
builder.Services.AddScoped<ICourseService, CourseService>();
builder.Services.AddAiClient(builder.Configuration); // "Ai:Provider": Claude | Gemini
builder.Services.AddScoped<ITeachingGuideGenerator, TeachingGuideGenerator>();
builder.Services.AddScoped<IStudySetService, StudySetService>();
builder.Services.AddScoped<ITestExportService, TestExportService>();
builder.Services.AddHttpClient<IKeycloakClient, KeycloakClient>();
builder.Services.AddScoped<IAuthService, AuthService>();

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

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

public partial class Program;
