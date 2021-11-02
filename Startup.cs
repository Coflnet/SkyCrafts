using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Coflnet.Sky.Crafts.Services;
using Jaeger.Samplers;
using Jaeger.Senders;
using Jaeger.Senders.Thrift;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using OpenTracing;
using OpenTracing.Util;
using Prometheus;

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

            services.AddControllers();
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo { Title = "SkyCrafts", Version = "v1" });
            });
            services.AddSingleton<CalculatorService>();
            services.AddSingleton<CraftingReceipeService>();
            services.AddSingleton<UpdaterService>();
            services.AddSingleton<CollectionService>();
            services.AddHostedService<UpdaterService>(provider => provider.GetService<UpdaterService>());

            services.AddSingleton<ITracer>(serviceProvider =>
            {
                ILoggerFactory loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
                IConfiguration iConfiguration = serviceProvider.GetRequiredService<IConfiguration>();

                Jaeger.Configuration.SenderConfiguration.DefaultSenderResolver = new SenderResolver(loggerFactory)
                        .RegisterSenderFactory<ThriftSenderFactory>();

                var samplingRate = 0.10d;
                var lowerBoundInSeconds = 30d;
                ISampler sampler = new GuaranteedThroughputSampler(samplingRate, lowerBoundInSeconds);
                var config = Jaeger.Configuration.FromIConfiguration(loggerFactory, iConfiguration);

                ITracer tracer = config.GetTracerBuilder()
                    .WithSampler(sampler)
                    .Build();

                GlobalTracer.Register(tracer);
                return tracer;
            });
            services.AddOpenTracing();
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

            app.UseHttpsRedirection();

            app.UseRouting();

            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapMetrics();
                endpoints.MapControllers();
            });
        }
    }
}
