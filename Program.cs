using HealthcareClaimsManager.Data;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddControllers();
builder.Services.AddSingleton<ReviewTaskRepository>();

var app = builder.Build();

// Ensure DB Stored Procedures are applied
//var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
//if (!string.IsNullOrEmpty(connectionString))
//{
//    DatabaseInitializer.EnsureStoredProceduresCreated(connectionString);
//}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

app.MapRazorPages();
app.MapControllers();

app.Run();