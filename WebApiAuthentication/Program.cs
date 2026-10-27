using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Scalar.AspNetCore;
using System.IdentityModel.Tokens.Jwt;
using System.Text;
using WebApiAuthentication.Authentication;
using WebApiAuthentication.DataAccess.Context;
using WebApiAuthentication.DataAccess.Repositories;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();

builder.Services.AddDbContext<ReviewContext>(options =>
                options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddIdentity<LibraryUser, IdentityRole>(options =>
    options.User.AllowedUserNameCharacters += " ")
           .AddEntityFrameworkStores<ReviewContext>()
           .AddDefaultTokenProviders();

builder.Services.AddScoped<IReviewRepository, SqlServerRepository>();

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
//builder.Services.AddOpenApi();

builder.Services.AddOpenApi("v1", options =>
{
    options.AddDocumentTransformer((document, context, cancellationToken) =>
    {
        // ── API metadata ──────────────────────────────────────────────
        document.Info = new OpenApiInfo
        {
            Title = "WebServer API",
            Version = "v1",
            Description = "Standalone API for Books Review"
        };

        // ── Bearer security scheme ────────────────────────────────────
        // Components and SecuritySchemes are null until explicitly initialised.
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();

        document.Components.SecuritySchemes["Bearer"] =
            new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "Enter your JWT token (without the 'Bearer' prefix)."
            };

        // ── Attach security requirement to every endpoint ─────────────
        // This makes the padlock icon appear on all operations in the UI.
        if (document.Paths is not null)
            foreach (var path in document.Paths.Values)
                if(path.Operations is not null)
                    foreach (var operation in path.Operations.Values)
                    {
                        // Security list is also null until initialised.
                        operation.Security ??= new List<OpenApiSecurityRequirement>();
                        operation.Security.Add(new OpenApiSecurityRequirement
                        {
                            // OpenApiSecuritySchemeReference links the requirement
                            // back to the named scheme defined in Components above.
                            [new OpenApiSecuritySchemeReference("Bearer", document)] = []
                        });
                    }

        return Task.CompletedTask;
    });
});

using var loggerFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Trace).AddConsole());

var secret = builder.Configuration["JWT:Secret"] ?? throw new InvalidOperationException("Secret not configured");

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options => 
{
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidIssuer = builder.Configuration["JWT:ValidIssuer"],
        ValidAudience = builder.Configuration["JWT:ValidAudience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
        ClockSkew = new TimeSpan(0, 0, 5)
    };
    options.Events = new JwtBearerEvents
    {
        OnChallenge = ctx => LogAttempt(ctx.Request.Headers, "OnChallenge"),
        OnTokenValidated = ctx => LogAttempt(ctx.Request.Headers, "OnTokenValidated")
    };
});

const string policy = "defaultPolicy";

builder.Services.AddCors(options =>
{
    options.AddPolicy(policy,
                      p =>
                      {
                          p.AllowAnyHeader();
                          p.AllowAnyMethod();
                          p.AllowAnyOrigin();
                      });
});

var app = builder.Build();

//PopulateDb();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    // Mounts the Scalar UI at /scalar/v1 (default route)
    app.MapOpenApi();
    app.MapScalarApiReference(options =>
    {
        options
        .WithTitle("")
        .WithTheme(ScalarTheme.BluePlanet)
        .AddPreferredSecuritySchemes("Bearer")
        .AddHttpAuthentication("Bearer", http => { });
    });
}

app.UseHttpsRedirection();

app.UseCors(policy);

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

Task LogAttempt(IHeaderDictionary headers, string eventType)
{
    var logger = loggerFactory.CreateLogger<Program>();

    var authorizationHeader = headers["Authorization"].FirstOrDefault();

    if (authorizationHeader is null)
        logger.LogInformation($"{eventType}. JWT not present");
    else
    {
        string jwtString = authorizationHeader.Substring("Bearer ".Length);

        var jwt = new JwtSecurityToken(jwtString);

        logger.LogInformation($"{eventType}. Expiration: {jwt.ValidTo.ToLongTimeString()}. System time: {DateTime.UtcNow.ToLongTimeString()}");
    }

    return Task.CompletedTask;
}

//void PopulateDb()
//{
//    using var scope = app.Services.CreateScope();
    
//    using var db = scope.ServiceProvider.GetRequiredService<ReviewContext>();

//    db.BookReviews.Add(new() { Title = "Dr No", Rating = 4 });
//    db.BookReviews.Add(new() { Title = "Goldfinger", Rating = 3 });
//    db.BookReviews.Add(new() { Title = "From Russia with Love", Rating = 1 });
//    db.BookReviews.Add(new() { Title = "Moonraker", Rating = 4 });
//    db.BookReviews.Add(new() { Title = "Dr No", Rating = 5 });
//    db.BookReviews.Add(new() { Title = "Moonraker", Rating = 2 });
//    db.BookReviews.Add(new() { Title = "Dr No", Rating = 2});
//    db.BookReviews.Add(new() { Title = "From Russia with Love", Rating = 5 });
//    db.BookReviews.Add(new() { Title = "From Russia with Love", Rating = 3 });

//    db.SaveChanges();
//}
