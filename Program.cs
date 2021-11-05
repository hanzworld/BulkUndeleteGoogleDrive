using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Events;

namespace BulkUndeleteGoogleDrive
{
    class Program
    {
        public static IConfigurationRoot Configuration { get; set; }
        static void Main(string[] args)
        {
            var builder = new ConfigurationBuilder();
            // tell the builder to look for the appsettings.json file
            builder.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
            builder.AddUserSecrets<Program>();

            Configuration = builder.Build();

            IServiceCollection services = new ServiceCollection();

            Log.Logger = new LoggerConfiguration()
            .WriteTo.File($"undelete-{DateTime.Now.ToString("yyy-M-dd--HH-mm-ss")}.log", LogEventLevel.Warning)
            .WriteTo.Console()
            .CreateLogger();

            //Map the implementations of your classes here ready for DI
            services
                .Configure<GoogleSecrets>(Configuration.GetSection("Google"))
                .AddOptions()
                .AddScoped<IUndeleter, Undeleter>()
                .AddLogging(configure => configure.AddSerilog());

            services.AddHttpClient<GoogleDriveService>((serviceProvider, client) =>
            {
                /* 
                this is an incredibly ugly hack but I have Google'd for two days straight and 
                can't figure out how this should be done properly. If you know, I'd love to hear from you!
                */
                var settings = serviceProvider.GetRequiredService<IOptions<GoogleSecrets>>().Value;
                /*
                to pass in the secrets to the third party Google constructor, we'll add 
                them as headers to the client (in full knowledge that this client is disposed 
                during construction of the GmailClient - see GmailClient implementation), 
                and then retrieve them on the other side. Ick.
                */
                client.DefaultRequestHeaders.Add(Constants.SecretsInHeadersHack.Id, settings.ClientId);
                client.DefaultRequestHeaders.Add(Constants.SecretsInHeadersHack.Secret, settings.ClientSecret);
            });

            var serviceProvider = services.BuildServiceProvider();

            var resolver = serviceProvider.GetService<IUndeleter>();

            var dateIStuffedUp = new DateTime(2021, 11, 05, 9, 0, 0, 0);
            resolver.UndeleteAfter(new DateTimeOffset(dateIStuffedUp, TimeZoneInfo.Local.GetUtcOffset(dateIStuffedUp)));
        }
    }

}
