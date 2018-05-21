using System;
using Flexinets.Core.Database.Models;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;
using Flexinets.iPass;
using System.Collections.Generic;
using log4net;
using System.IO;
using System.Reflection;
using Microsoft.Extensions.Configuration;

namespace Flexinets.Ipass.ActivationTokenRefreshFunction
{
    public static class Function1
    {
        private static readonly ILog _log = LogManager.GetLogger(typeof(Function1));


        [FunctionName("ActivationTokenRefreshFunction")]
        public static async Task Run([TimerTrigger("0 0 0 */1 * *")]TimerInfo myTimer, TraceWriter log, ExecutionContext context)
        {
            var config = new ConfigurationBuilder().SetBasePath(context.FunctionAppDirectory).AddJsonFile("local.settings.json", optional: true, reloadOnChange: true).AddEnvironmentVariables().Build();
            log4net.Config.XmlConfigurator.ConfigureAndWatch(LogManager.GetRepository(Assembly.GetEntryAssembly()), new FileInfo(Path.Combine(context.FunctionAppDirectory, "log4net.config")));
            log.Info("Configuration read");

            try
            {
                var client = new iPassProvisioningApiClient(Environment.GetEnvironmentVariable("Ipass:apikey"), Environment.GetEnvironmentVariable("Ipass:servicebusconnectionstring"));

                log.Info("Creating context");
                var contextFactory = new FlexinetsContextFactory(Environment.GetEnvironmentVariable("FlexinetsContext"));
                IEnumerable<Users> users;

                log.Info("Getting users");
                using (var db = contextFactory.CreateContext())
                {
                    users = await db.Users.Include(o => o.Node).Where(o => o.Status == 1 && o.HostedAuthId != null && o.Activationurldate < DateTime.UtcNow.AddDays(-10)).AsNoTracking().ToListAsync();
                }

                _log.Info($"Refreshing {users.Count()} hosted user tokens");

                foreach (var u in users)
                {
                    try
                    {
                        log.Info($"Refreshing activation token for {u.UsernameDomain}");
                        using (var db = contextFactory.CreateContext())
                        {
                            var user = await db.Users.SingleOrDefaultAsync(o => o.UserId == u.UserId);
                            var newUrl = await client.RefreshActivationUrl(u.Node.IpassCustomerId.Value, u.UsernameDomain);
                            user.Activationurl = newUrl;
                            user.Activationurldate = DateTime.UtcNow;
                            await db.SaveChangesAsync();
                        }
                    }
                    catch (Exception ex)
                    {
                        log.Error($"Couldnt refresh token for username {u.UsernameDomain}", ex);
                    }
                }
            }
            catch (Exception ex)
            {
                log.Error($"Something went horribly wrong: {ex.Message}", ex);
            }
        }
    }
}