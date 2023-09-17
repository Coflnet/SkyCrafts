using System.Text.Json.Serialization;
using Coflnet.Sky.Crafts.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi.Models;
using Prometheus;
using Coflnet.Core;

namespace Coflnet.Sky.Crafts
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {

            services.AddControllers().AddJsonOptions(json=>{
                json.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
            });
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo { Title = "SkyCrafts", Version = "v1" });
            });
            services.AddSingleton<CalculatorService>();
            services.AddSingleton<CraftingRecipeService>();
            services.AddSingleton<UpdaterService>();
            services.AddSingleton<CollectionService>();
            services.AddSingleton<KatUpgradeService>();
            services.AddSingleton<Api.Client.Api.IPricesApi>(provider => new Api.Client.Api.PricesApi(Configuration["API_BASE_URL"]));
            services.AddSingleton<IReforgeService,ReforgeService>();
            services.AddHostedService<UpdaterService>(provider => provider.GetService<UpdaterService>());

            services.AddCoflnetCore();
            services.AddResponseCaching();
            services.AddMemoryCache();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            app.UseSwagger();
            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "SkyCrafts v1");
                c.RoutePrefix = "api";
            });

            app.UseResponseCaching();

            app.UseRouting();

            app.UseCoflnetCore();

            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapMetrics();
                endpoints.MapControllers();
            });
        }
    }
}
