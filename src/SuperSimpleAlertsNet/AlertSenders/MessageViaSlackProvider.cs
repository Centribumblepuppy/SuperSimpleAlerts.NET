using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Amazon.Lambda.Core;
using Microsoft.Extensions.Configuration;

namespace SuperSimpleAlertsNet
{
    public class MessageViaSlackProvider : IAlertSender
    {
        public bool IsInitialized { get; private set; }

        public const string CODE = "slack";

        private HttpClient _httpClient;
        private AlertingConfig _config;

        public Task Init(IConfiguration rawConfig, AlertingConfig config)
        {
            _httpClient = new HttpClient();
            _config = config;

            IsInitialized = true;

            return Task.CompletedTask;
        }

        public async Task Send(string endpoint, string alertLevel, string alertCode, string message,
            ILambdaLogger contextLogger)
        {
            try
            {
                var color = GetColor(alertLevel, alertCode);

                var postData = new Dictionary<string, string>
                {
                    {"payload", $@"{{ ""attachments"": [ {{ ""text"":""*{alertLevel}* > *{alertCode}*: {message}"", ""color"":""{color}"" }}] }}"}
                };

                var response = await _httpClient.PostAsync(
                    endpoint,
                    new FormUrlEncodedContent(postData));

                if (response.StatusCode != HttpStatusCode.OK)
                {
                    contextLogger.LogLine(
                        $"Error sending message to slack. Status code: {response.StatusCode}. Response text: {response.Content.ReadAsStringAsync()}");
                }
            }
            catch (Exception ex)
            {
                contextLogger.LogLine(ex.ToString());
            }
        }

        // TODO Make configurable
        private string GetColor(string alertLevel, string alertCode)
        {
            var configuredColoring = _config?.GetMatchingHandlers(
                    alertCode, alertLevel)
                    ?.FirstOrDefault(x => x.Handler == CODE)
                    ?.Coloring;

            if (!string.IsNullOrEmpty(configuredColoring))
                return configuredColoring;
            
            if (string.IsNullOrEmpty(alertLevel))
                return "#000000";

            switch (alertLevel.ToLower())
            {
                case "critical":
                    return "danger";
                case "warning":
                    return "warning";
                case "info":
                    return "good";
            }

            return "#000000";
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
            _httpClient = null;
        }
    }
}
