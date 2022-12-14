using Azure;
using Azure.Identity;
using Azure.Monitor.Query;
using Azure.Monitor.Query.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HealthCheck
{
    [Route("api/[controller]")]
    [ApiController]
    public class HealthCheckController : ControllerBase
    {
        private static readonly decimal baseLineFailedRate = 10;
        private static readonly LogsQueryClient client = new(new VisualStudioCredential());
        private static readonly string workspaceId = "";
        private static readonly string dependecy_graph_query = @"(AppDependencies 
                | where AppRoleName == '{0}' 
                | summarize count(),avg(DurationMs) by Target,AppRoleName,DependencyType) 
                | join kind=leftouter (AppDependencies 
                    | where AppRoleName == '{0}' and Success != true 
                    | summarize count(),avg(DurationMs) by Target,AppRoleName,DependencyType) 
                on Target,AppRoleName,DependencyType
                | project 
                    Target, 
                    DependencyType,
                    avg_DurationMs,
                    total=todecimal(count_),
                    failed = iff(isnull(count_1),decimal(0),todecimal(count_1))";

        public async Task<IActionResult> Get()
        {
            var instanceName = "sim-jrc-bna-maintenance-svc-aue";
            var criticalComponents = new Dictionary<string, HealthData> {
                { "queue.core.windows.net", new( HealthStatus.Degraded,"Storage Queue") },
                { "blob.core.windows.net",new( HealthStatus.Unhealthy, "Blob Storage") },
                { "table.core.windows.net",new( HealthStatus.Unhealthy, "Table Storage") },
                { "documents.azure.com", new(HealthStatus.Unhealthy, "CosmosDB") },
                { "xero.com",new(HealthStatus.Unhealthy, "Xero") },
                { "pam-web-svc-aue.azurewebsites.net",new(HealthStatus.Unhealthy,"PAM") },
                { "stripe.com", new(HealthStatus.Unhealthy,"Stripe") },
                { "identitysimulation.bricksandagent.com", new(HealthStatus.Degraded,"Identity Server") },
                { "database.windows.net",new( HealthStatus.Degraded, "Database") },
                { "servicebus.windows.net",new( HealthStatus.Degraded, "Service Bus") }
            };
            string query = string.Format(dependecy_graph_query, instanceName);
            Response<LogsQueryResult> response = await client.QueryWorkspaceAsync(workspaceId, query, new QueryTimeRange(TimeSpan.FromDays(1)));

            LogsTable table = response.Value.Table;

            var entries = new Dictionary<string, EntryHealthStatus>();
            foreach (var row in table.Rows)
            {
                var target = row[0].ToString().Split("|")[0].Trim();
                var key = criticalComponents.Keys.FirstOrDefault(key => target.Contains(key));
                if (key is null) continue;

                var total = int.Parse(row[3].ToString());
                var failed = int.Parse(row[4].ToString());
                var dependencyType = row[1].ToString();
                var durationMs = TimeSpan.FromMilliseconds(double.Parse(row[2].ToString()));

                var criticalComponent = criticalComponents[key];
                if (entries.TryGetValue(criticalComponent.Name, out EntryHealthStatus entry))
                {
                    if (!entry.Tags.Contains(dependencyType)) _ = entry.Tags.Add(dependencyType);
                    entry.Total += total;
                    entry.Failed += failed;
                    entry.Status = (entry.FailedRate > baseLineFailedRate ? criticalComponent.Status : HealthStatus.Healthy).ToString();
                }
                else
                {
                    entry = new EntryHealthStatus
                    {
                        Tags = new HashSet<string> { dependencyType },
                        Duration = durationMs,
                        Total = total,
                        Failed = failed,
                        Description = key
                    };
                    entry.Status = (entry.FailedRate > baseLineFailedRate ? criticalComponent.Status : HealthStatus.Healthy).ToString();
                    entries.Add(criticalComponent.Name, entry);
                }
            }

            return Ok(new
            {
                status = (entries.Any(e => e.Value.Status == HealthStatus.Degraded.ToString()) ? HealthStatus.Degraded
                    : entries.Any(e => e.Value.Status == HealthStatus.Unhealthy.ToString()) ? HealthStatus.Unhealthy : HealthStatus.Healthy)
                    .ToString(),
                entries
            });
        }
    }
}

internal class EntryHealthStatus
{
    public string Status { get; set; }
    public HashSet<string> Tags { get; set; } = new HashSet<string>();
    public TimeSpan Duration { get; set; }
    public int Total { get; set; }
    public int Failed { get; set; }
    public decimal FailedRate => (Failed / Convert.ToDecimal(Total)) * 100;
    public string Description { get; set; }
}

internal class HealthData
{
    public HealthData(HealthStatus status, string name)
    {
        Status = status;
        Name = name;
    }

    public HealthStatus Status { get; set; }
    public string Name { get; set; }
}
