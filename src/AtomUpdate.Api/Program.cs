using AtomUpdate.Api;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAtomUpdateApi(builder.Configuration, builder.Environment.ContentRootPath);

var app = builder.Build();

await app.UseAtomUpdateApiAsync();

app.MapAtomUpdateApi();

app.Run();

public partial class Program;
