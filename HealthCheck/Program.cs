var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
var services = builder.Services;
services
    .AddHealthChecksUI()
    .AddInMemoryStorage();
services.AddControllers();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHealthChecksUI(config => { config.UIPath = "/hc-ui"; });
app.UseHttpsRedirection();

app.UseRouting()
    .UseEndpoints(endpoints => endpoints.MapControllers());
app.Run();
