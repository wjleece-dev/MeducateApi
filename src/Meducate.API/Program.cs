using Meducate.API.DependencyInjection;
using Meducate.API.Infrastructure;
using Meducate.Infrastructure.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddMeducateInfrastructure(builder.Configuration)
    .AddMeducateApi(builder.Configuration, builder.Environment)
    .AddMeducateAuth();

var app = builder.Build();

await app.ApplyMigrationsAsync();

app.UseMeducatePipeline();

app.Run();
