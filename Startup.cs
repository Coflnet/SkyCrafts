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
using Coflnet.Sky.Core.Services;

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
            services.AddSingleton<CalculatorService>();
            services.AddSingleton<CraftingRecipeService>();
            services.AddSingleton<UpdaterService>();
            services.AddSingleton<CollectionService>();
            services.AddSingleton<KatUpgradeService>();
            services.AddSingleton<ForgeCraftService>();
            services.AddSingleton<RequirementService>();
            services.AddSingleton<HypixelItemService>();
            services.AddHttpClient();
            services.AddSingleton<Api.Client.Api.IPricesApi>(provider => new Api.Client.Api.PricesApi(Configuration["API_BASE_URL"]));
            services.AddSingleton<Api.Client.Api.IItemApi>(provider => new Api.Client.Api.ItemApi(Configuration["API_BASE_URL"]));
            services.AddSingleton<Items.Client.Api.IItemsApi>(provider => new Items.Client.Api.ItemsApi(Configuration["ITEMS_BASE_URL"]));
            services.AddSingleton<PlayerState.Client.Api.IItemsApi>(provider => new PlayerState.Client.Api.ItemsApi(Configuration["PLAYERSTATE_BASE_URL"]));
            services.AddSingleton<IReforgeService,ReforgeService>();
            services.AddSingleton<PriceDropService>();
            services.AddSingleton<GeorgePetOfferService>();
            services.AddSingleton<GeorgeFlipService>();
            services.AddSingleton<NpcSellService>();
            services.AddHostedService<UpdaterService>(provider => provider.GetService<UpdaterService>());
            services.AddHostedService<NpcSellRefresher>();

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
