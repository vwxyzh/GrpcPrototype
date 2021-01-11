using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebSockets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Proxy
{
    public class Startup
    {
        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {
            var c = Channel.CreateBounded<ClientConnect>(10);
            var d = Channel.CreateBounded<ClientDisconnect>(10);
            var m = new ConcurrentDictionary<string, Channel<ClientMessage>>();

            services.AddSingleton(c.Reader);
            services.AddSingleton(d.Reader);
            services.AddSingleton(m);
            services.AddSingleton(c.Writer);
            services.AddSingleton(d.Writer);
            services.AddSingleton(m);
            services.AddSingleton<WSLifetimeManager>();
            services.AddGrpc();
            services.AddWebSockets(o =>
            {
                o.AllowedOrigins.Add("*");
            });
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseRouting();
            app.UseWebSockets();
            app.Use(async (context, next) =>
            {
                if (context.Request.Path == "/client")
                {
                    if (context.WebSockets.IsWebSocketRequest)
                    {
                        using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
                        var lm = app.ApplicationServices.GetRequiredService<WSLifetimeManager>();
                        await lm.HandleWebSocketAsync(context, webSocket);
                    }
                    else
                    {
                        context.Response.StatusCode = 400;
                    }
                }
                else
                {
                    await next();
                }
            });

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapGrpcService<GrpcServerConnection>();
                endpoints.MapGet("/", async context =>
                {
                    await context.Response.WriteAsync("test");
                });
            });
        }
    }
}
