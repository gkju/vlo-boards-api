using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using AccountsData.Data;
using AccountsData.Models.DataModels;
using IdentityModel.AspNetCore.AccessTokenValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using Microsoft.IdentityModel.Tokens;
using Minio;

namespace vlo_boards_api
{
    public class Startup
    {
        public Startup(IConfiguration configuration, IWebHostEnvironment environment)
        {
            Configuration = configuration;
            env = environment;
        }

        public IConfiguration Configuration { get; }
        public IWebHostEnvironment env { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            string authAuthority = "",
                authAudience = "",
                authSecret = "",
                migrationsAssembly = "",
                minioEndpoint = "",
                minioSecret = "",
                minioAccessToken = "",
                bucketName = "boards",
                appDbCS = "";
            List<string> corsorigins = new List<string>();
            if (env.IsDevelopment())
            {
                authAuthority = Configuration["Auth:Authority"];
                authAudience = Configuration["Auth:Audience"];
                authSecret = Configuration["Auth:Secret"];
                migrationsAssembly = typeof(Startup).GetTypeInfo().Assembly.GetName().Name;

                minioEndpoint = Configuration["minio:endpoint"];
                minioSecret = Configuration["minio:secret"];
                minioAccessToken =  Configuration["minio:access"];
                
                appDbCS = Configuration.GetConnectionString("NPGSQL");
                corsorigins = new List<string> {"http://localhost:44328", "https://localhost:44328", "http://localhost:3000", "https://localhost:3000"};
            }

            services.AddTransient<MinioConfig>(o => new MinioConfig {BucketName = bucketName});

            services.AddTransient<MinioClient>((o) =>
            {
                MinioClient minioClient;
                if (env.IsDevelopment())
                {
                    minioClient = new MinioClient(minioEndpoint, minioAccessToken, minioSecret);
                }
                else
                {
                    minioClient = new MinioClient(minioEndpoint, minioAccessToken, minioSecret).WithSSL();
                }

                return minioClient;
            });
            
            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseNpgsql(appDbCS, sql => sql.MigrationsAssembly(migrationsAssembly)));
            
            services.AddControllers();
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo {Title = "vlo_boards_api", Version = "v1"});
            });

            var dbContext = services.BuildServiceProvider()
                .GetService<ApplicationDbContext>();
            
            services.AddIdentityCore<ApplicationUser>()
                .AddEntityFrameworkStores<ApplicationDbContext>()
                .AddDefaultTokenProviders()
                .AddUserManager<UserManager<ApplicationUser>>();
            
            services.TryAddSingleton<IHttpContextAccessor, HttpContextAccessor>();

            services.AddAuthentication("token")
                .AddJwtBearer("token", options =>
                {
                    options.Authority = authAuthority;
                    options.Audience = authAudience;

                    options.TokenValidationParameters.ValidTypes = new[] { "at+jwt" };

                    //reference token
                    options.ForwardDefaultSelector = Selector.ForwardReferenceToken("introspection");
                })
                .AddOAuth2Introspection("introspection", options =>
                {
                    options.Authority = authAuthority;

                    options.ClientId =  authAudience;
                    options.ClientSecret = authSecret;
                });
            
            services.AddAuthorization(options =>
            {
                options.AddPolicy("VLO_BOARDS_API_SCOPE", policy =>
                {
                    policy.RequireAuthenticatedUser();
                    policy.RequireClaim("scope", "VLO_BOARDS");
                });
            });
            
            services.AddCors(options =>
            {
                options.AddPolicy(name: "DefaultExternalOrigins",
                    builder =>
                    {
                        builder.WithOrigins(corsorigins.ToArray())
                            .AllowAnyHeader()
                            .AllowAnyMethod();
                    });
            });
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseSwagger();
                app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "vlo_boards_api v1"));
            }

            app.UseHttpsRedirection();

            app.UseRouting();

            app.UseCors("DefaultExternalOrigins");

            app.UseAuthentication();
            app.UseAuthorization();

            //base scope is required for every endpoint in this api
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllerRoute(
                        name: "default",
                        pattern: "{controller}/{action=Index}/{id?}").RequireAuthorization("VLO_BOARDS_API_SCOPE");
            });
        }
    }
}