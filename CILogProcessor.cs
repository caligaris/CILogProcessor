using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Azure.EventHubs;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;

namespace CILogProcessor
{
    public static class CILogProcessor
    {
        static HttpClient _client = new HttpClient();

        // Update WorkspaceId to your Log Analytics workspace ID
        static string WorkspaceId = Environment.GetEnvironmentVariable("WORKSPACEID");
        // For sharedKey, use either the primary or the secondary Connected Sources client authentication key
        static string sharedKey = Environment.GetEnvironmentVariable("SHAREDKEY");
        // LogName is name of the event type that is being submitted to Azure Monitor
        static string LogName = Environment.GetEnvironmentVariable("LOG_NAME");
        // You can use an optional field to specify the timestamp from the data. If the time field is not specified, Azure Monitor assumes the time is the message ingestion time
        static string TimeStampField = Environment.GetEnvironmentVariable("TIMEGENERATED_FIELD") ?? "time";
        // PROPSTOEXCLUDE is a comma separated string that can be use to remove the identity property to avoid storing personally identifiable information data
        static List<string> PropsToExclude = Environment.GetEnvironmentVariable("PROPSTOEXCLUDE")?.Split(",").ToList<string>() 
            ?? new List<string>();
        public static string BuildSignature(string message, string datestring)
        {
            // Create a hash for the API signature
            var jsonBytes = Encoding.UTF8.GetBytes(message);
            string stringToHash = "POST\n" + jsonBytes.Length + "\napplication/json\n" + "x-ms-date:" + datestring + "\n/api/logs";

            var encoding = new System.Text.ASCIIEncoding();
            byte[] keyByte = Convert.FromBase64String(sharedKey);
            byte[] messageBytes = encoding.GetBytes(stringToHash);
            using (var hmacsha256 = new HMACSHA256(keyByte))
            {
                byte[] hash = hmacsha256.ComputeHash(messageBytes);
                return "SharedKey " + WorkspaceId + ":" + Convert.ToBase64String(hash);
            }
        }

        // Send a request to the POST API endpoint
        public static async Task<HttpResponseMessage> PostData(string signature, string date, string json)
        {
            string url = "https://" + WorkspaceId + ".ods.opinsights.azure.com/api/logs?api-version=2016-04-01";

            System.Net.Http.HttpClient _client = new System.Net.Http.HttpClient();
            _client.DefaultRequestHeaders.Add("Accept", "application/json");
            _client.DefaultRequestHeaders.Add("Log-Type", LogName);
            _client.DefaultRequestHeaders.Add("Authorization", signature);
            _client.DefaultRequestHeaders.Add("x-ms-date", date);
            _client.DefaultRequestHeaders.Add("time-generated-field", TimeStampField);

            System.Net.Http.HttpContent httpContent = new StringContent(json, Encoding.UTF8);
            httpContent.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            return await _client.PostAsync(new Uri(url), httpContent);
        }

        [FunctionName("CILogProcessor")]
        public static async Task Run(
            [EventHubTrigger("insight-log-audit", Connection = "EVENTHUB_CONNECTION_STRING")] EventData[] events,
            ILogger log)
        {
            try
            {
                //Map EventData[] to Array of string
                var messages = events.Select<EventData, string>(e =>
                {
                    //Encode EventData body to string
                    var message = Encoding.UTF8.GetString(e.Body.Array, e.Body.Offset, e.Body.Count);
                    var record = JsonSerializer.Deserialize<JsonElement>(message);
                    //Remove shallow properties like idenity 
                    if (PropsToExclude.Count > 0){
                        var filtered = record.EnumerateObject().Where(r => {
                            return !PropsToExclude.Contains(r.Name);
                        });
                        //Join object JsonProperties array to json object as string
                        message = "{" + String.Join(",", filtered) + "}";
                    }
                    return message;
                });

                var jsonMessages = $"[{String.Join(",", messages)}]";
                log.LogInformation($"Json Message to post: {jsonMessages}");

                //Generate signature as specified in Log Analytics Data collector API 
                //https://docs.microsoft.com/en-us/azure/azure-monitor/platform/data-collector-api#sample-requests
                var datestring = DateTime.UtcNow.ToString("r");
                var signature = BuildSignature(jsonMessages, datestring);

                var response = await PostData(signature, datestring, jsonMessages);
                var responseContent = response.Content;
                string result = await responseContent.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode){
                    log.LogInformation($"Failed to send request to Log Analytics Data Collector, response: {result}");
                    throw new Exception($"Failed to send request to Log Analytics Data Collector, response: {result}");
                }
            }
            catch (Exception e) 
            {
                log.LogError($"Exception when posting messages, error: {e.Message}");
                throw e;
            }
        }
    }
}
