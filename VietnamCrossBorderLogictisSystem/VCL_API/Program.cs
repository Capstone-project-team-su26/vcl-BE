using System.Linq;
using VCL_API.Injection;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

// Mỗi lệnh ghi chạy trong một transaction: lỗi giữa chừng thì rollback cả thao tác.
builder.Services.AddScoped<VCL_API.Infrastructure.DbTransactionFilter>();
builder.Services.AddControllers(options =>
    {
        options.Filters.AddService<VCL_API.Infrastructure.DbTransactionFilter>();
    })
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new VCL_API.Helpers.DateTimeJsonConverter());
    });
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();

// Add CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.SetIsOriginAllowed(origin => true)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials()
              .WithExposedHeaders("*");
    });
    options.AddPolicy("AllowAll", policy =>
    {
        policy.SetIsOriginAllowed(origin => true)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials()
              .WithExposedHeaders("*");
    });
});

// Add project services
builder.Services.AddProjectServices(builder.Configuration);

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseSwagger();
app.UseSwaggerUI();

app.UseCors("AllowAll");

// app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<VCL_API.Hubs.ChatHub>("/hubs/chat");

app.Run();
