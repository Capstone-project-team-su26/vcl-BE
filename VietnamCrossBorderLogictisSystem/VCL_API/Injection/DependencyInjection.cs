using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VCL_DLL.Interface;
using VCL_DLL.Models;
using VCL_DLL.Repositories;
using VCL_BLL.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Microsoft.OpenApi.Models;
using VCL_BLL.Interface;

namespace VCL_API.Injection
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddProjectServices(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddDbContext<VCLDBContext>(options =>
                options.UseSqlServer(configuration.GetConnectionString("DefaultConnection")));

            services.AddScoped(typeof(IGenericRepository<>), typeof(GenericRepository<>));
            services.AddScoped<IUnitOfWork, UnitOfWork>();

            services.AddScoped<IAuthService, AuthService>();
            services.AddScoped<IEmailService, EmailService>();
            services.AddScoped<IUserService, UserService>();
            services.AddScoped<IQuotationService, QuotationService>();
            services.AddScoped<IOrderService, OrderService>();
            services.AddScoped<IRestrictedItemService, RestrictedItemService>();
            services.AddScoped<ICarrierService, CarrierService>();
            services.AddScoped<IPricingRuleService, PricingRuleService>();
            services.AddScoped<IServicePricingService, ServicePricingService>();
            services.AddScoped<IParcelService, ParcelService>();
            services.AddScoped<IInventoryService, InventoryService>();
            services.AddScoped<IWroService, WroService>();
            services.AddScoped<IPickingListService, PickingListService>();
            services.AddScoped<ICustomerService, CustomerService>();
            services.AddScoped<IWarehouseReceivingNoteService, WarehouseReceivingNoteService>();
            services.AddScoped<IWarehouseService, WarehouseService>();
            services.AddScoped<IDeliveryAddressService, DeliveryAddressService>();
            services.AddScoped<IFileUploadService, CloudinaryFileUploadService>();
            services.AddScoped<IConsolidationService, ConsolidationService>();
            services.AddScoped<IPurchaseRequestService, PurchaseRequestService>();
            services.AddScoped<IProductTypeService, ProductTypeService>();
            services.AddScoped<IInternationalShipmentService, InternationalShipmentService>();
            services.AddScoped<IShippingMethodService, ShippingMethodService>();
            services.AddScoped<IConversationService, ConversationService>();

            services.AddSignalR();

            // Add JWT Authentication
            var jwtSettings = configuration.GetSection("JWT");
            var secretKey = jwtSettings["SecretKey"];
            var key = Encoding.UTF8.GetBytes(secretKey);

            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(key),
                    ValidateIssuer = true,
                    ValidIssuer = jwtSettings["ValidIssuer"],
                    ValidateAudience = true,
                    ValidAudience = jwtSettings["ValidAudience"],
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero
                };

                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        var accessToken = context.Request.Query["access_token"];
                        var path = context.HttpContext.Request.Path;
                        if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs/chat"))
                        {
                            context.Token = accessToken;
                        }
                        return Task.CompletedTask;
                    }
                };
            });

            // Add Swagger Gen with JWT Bearer
            services.AddSwaggerGen(c =>
            {
                c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
                {
                    Description = "Enter your JWT token only.",
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "JWT"
                });

                c.AddSecurityRequirement(new OpenApiSecurityRequirement
                {
                    {
                        new OpenApiSecurityScheme
                        {
                            Reference = new OpenApiReference
                            {
                                Type = ReferenceType.SecurityScheme,
                                Id = "Bearer"
                            }
                        },
                        new List<string>()
                    }
                });

                // Load XML documentation comments for API
                var xmlFileApi = $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml";
                var xmlPathApi = System.IO.Path.Combine(System.AppContext.BaseDirectory, xmlFileApi);
                if (System.IO.File.Exists(xmlPathApi))
                {
                    c.IncludeXmlComments(xmlPathApi);
                }

                // Load XML documentation comments for BLL (DTOs)
                var xmlFileBll = "VCL_BLL.xml";
                var xmlPathBll = System.IO.Path.Combine(System.AppContext.BaseDirectory, xmlFileBll);
                if (System.IO.File.Exists(xmlPathBll))
                {
                    c.IncludeXmlComments(xmlPathBll);
                }
            });

            return services;
        }
    }
}
