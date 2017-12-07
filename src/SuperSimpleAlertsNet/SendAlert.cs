using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Amazon;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Amazon.Lambda.SNSEvents;
using Amazon.S3;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using SuperSimpleAlertsNet.Statefulness;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.Json.JsonSerializer))]

namespace SuperSimpleAlertsNet
{
    public class SendAlert : IDisposable
    {
        private readonly IAmazonS3 _s3Client;
        private readonly IDictionary _environmentVariables;
        private readonly IDictionary<string, IAlertSender> _alertSenders;
        private IConfiguration _configuration;
        private AlertingConfig _options;
        private readonly IStatefulness _statefulness;

        /// <summary>
        /// Default constructor. This constructor is used by Lambda to construct the instance. When invoked in a Lambda environment
        /// the AWS credentials will come from the IAM role associated with the function and the AWS region will be set to the
        /// region the Lambda function is executed in.
        /// </summary>
        public SendAlert()
        {
            _alertSenders = new Dictionary<string, IAlertSender>
            {
                {SmsViaTwilioProvider.CODE, new SmsViaTwilioProvider()},
                {EmailViaAWSSESProvider.CODE, new EmailViaAWSSESProvider()},
                {MessageViaSlackProvider.CODE, new MessageViaSlackProvider()}
            };
            _environmentVariables = Environment.GetEnvironmentVariables();
            _s3Client = new AmazonS3Client(RegionEndpoint.USEast2);
            _statefulness = new RedisStatefulness(_environmentVariables);
        }

        /// <summary>
        /// Constructs an instance with a pre-configured S3 client. This can be used for testing the outside of the Lambda environment.
        /// </summary>
        public SendAlert(IAmazonS3 s3Client, IDictionary environmentVariables, IDictionary<string, IAlertSender> providers, IStatefulness statefulness)
        {
            _s3Client = s3Client;
            _environmentVariables = environmentVariables;
            _alertSenders = providers;
            _statefulness = statefulness;
        }

        public Task<APIGatewayProxyResponse> ReadSNSEvent(SNSEvent snsEvent, ILambdaContext context)
        {
            var message = snsEvent.Records.Select(x => x.Sns.Message).FirstOrDefault(x => !string.IsNullOrEmpty(x));

            context.Logger.LogLine("Send alert invoked. Message: " + message);

            var input = JsonConvert.DeserializeObject<Input>(message);

            return Send(input, context);
        }

        /// <summary>
        /// This method is called for every Lambda invocation. This method takes in an S3 event object and can be used 
        /// to respond to S3 notifications.
        /// </summary>
        //public async Task<APIGatewayProxyResponse> Send(SNSEvent snsEvent, ILambdaContext context)
        public async Task<APIGatewayProxyResponse> Send(Input input, ILambdaContext context)
        {
            await EnsureOptionsBound(context);

            if (_options == null)
            {
                return new APIGatewayProxyResponse
                {
                    StatusCode = 500,
                    Body = "Error - see CloudTrail logs for details"
                };
            }

            try
            {
                if (ShouldDeduplicateAlert(input))
                {
                    return new APIGatewayProxyResponse
                    {
                        StatusCode = 200,
                        Body = "Skipped duplicate alert with code: " + input.AlertCode
                    };
                }
            }
            catch (Exception ex)
            {
                context.Logger.LogLine("Exception checking for duplicate alert: " + ex);
            }

            try
            {               
                await HandleSubscriptions(input, context.Logger);

                return new APIGatewayProxyResponse
                {
                    StatusCode = 200,
                    Body = "Send alert invoked with alert code: " + input.AlertCode
                };
            }
            catch (Exception ex)
            {
                context.Logger.LogLine("Exception sending alert: " + ex);
                return new APIGatewayProxyResponse
                {
                    StatusCode = 500,
                    Body = "Error - see CloudTrail logs for details"
                };
            }
        }

        private async Task EnsureOptionsBound(ILambdaContext context)
        {
            if (_options != null) return;

            try
            {
                if (_configuration == null)
                    _configuration = await GetConfiguration();

                _options = new AlertingConfig();

                _configuration.Bind(_options);

                _options.Validate();
            }
            catch (Exception ex)
            {
                context.Logger.LogLine("Exception processing configuration: " + ex);
                _options = null;
            }
        }

        private bool ShouldDeduplicateAlert(Input input)
        {
            var deduplicatePerSeconds = _options.Deduplicate
                                            .GetSpecific(input.AlertCode, input.AlertLevel)
                                            ?.DeduplicatePerSecond
                                        ?? _options.Deduplicate.DeduplicatePerSecond;

            if (deduplicatePerSeconds <= 0) return false;

            if (_statefulness.ShouldDeduplicate(input.AlertCode)) return true;
                
            _statefulness.SetAlertWasSent(input.AlertCode, TimeSpan.FromSeconds(deduplicatePerSeconds));

            return false;
        }

        private async Task HandleSubscriptions(Input input, ILambdaLogger contextLogger)
        {
            var matchingHandlers = _options.GetMatchingHandlers(input.AlertCode, input.AlertLevel);

            if (matchingHandlers == null)
            {
                contextLogger.LogLine($"No matching subscriptions for alert code {input.AlertCode} or level {input.AlertLevel}.");
                return;
            }
            
            // Get required tasks and handlers
            // TODO Was having heaps of trouble running tasks in parallel, mostly because AWS SMTP is a bitch. At least should run SMS, email, Slack etc in parallel
            foreach (var handler in matchingHandlers)
            {
                var message = input.TextFor
                    ?.Where(x => x.Handler.Equals(handler.Handler, StringComparison.OrdinalIgnoreCase))
                    .Select(x => x.Text)
                    .FirstOrDefault();

                if (string.IsNullOrEmpty(message))
                    message = input.DefaultText;

                if (!string.IsNullOrEmpty(message))
                {
                    if (!_alertSenders.ContainsKey(handler.Handler))
                        throw new InvalidOperationException("Unknown handler: " + handler.Handler);

                    var provider = _alertSenders[handler.Handler];

                    if (!provider.IsInitialized)
                        await provider.Init(_configuration, _options);
                    
                    var matchingEndpoints = GetMatchingEndpoints(_options, handler.Contacts, handler.Handler).ToList();

                    if (!matchingEndpoints.Any())
                        contextLogger.LogLine("No matching configured endpoints for " + handler.Handler);

                    foreach (var endpoint in matchingEndpoints)
                    {
                        contextLogger.LogLine($"Sending alert with provider {provider.GetType()}");
                        try
                        {
                            await provider.Send(endpoint, input.AlertLevel, input.AlertCode, message, contextLogger);
                        }
                        catch (Exception ex)
                        {
                            contextLogger.LogLine(ex.ToString());
                        }
                    }
                }
                else
                {
                    contextLogger.LogLine("No text specified for handler " + handler.Handler);
                }
            }
        }

        private async Task<IConfiguration> GetConfiguration()
        {
            var s3BucketName = (string) _environmentVariables["configurationFileS3Bucket"];
            var s3Key = (string) _environmentVariables["configurationFileS3Key"];

            if (string.IsNullOrEmpty(s3BucketName))
                throw new InvalidOperationException("configurationFileS3Bucket not found in environment variables");
            if (string.IsNullOrEmpty(s3Key))
                throw new InvalidOperationException("configurationFileS3Key not found in environment variables");

            var configFile = await _s3Client.GetObjectAsync(s3BucketName, s3Key);

            if (configFile.HttpStatusCode != HttpStatusCode.OK)
                throw new InvalidOperationException("Could not get configuration from S3. Status code: " + configFile.HttpStatusCode);

            string configJson;

            using (var responseStream = configFile.ResponseStream)
            using (var reader = new StreamReader(responseStream))
            {
                configJson = reader.ReadToEnd();
            }

            // TODO Decrypt the file
            return new ConfigurationBuilder()
                .Add(new JsonStringConfigurationSource(configJson))
                .Build();
        }

        public IEnumerable<string> GetMatchingEndpoints(AlertingConfig options, IEnumerable<string> contacts, string endpointType)
        {
            return contacts
                .SelectMany(contact =>
                    options
                        .Contacts
                        ?.Where(c => c != null
                                     && c.Name.Equals(contact, StringComparison.OrdinalIgnoreCase))
                        .SelectMany(x =>
                            x.EndPoints.Where(ep => ep.Type.Equals(endpointType, StringComparison.OrdinalIgnoreCase)))
                        .Select(ep => ep.Value)
                        .Where(v => !string.IsNullOrEmpty(v)))
                .Where(x => !string.IsNullOrEmpty(x));
        }

        public void Dispose()
        {
            if (_alertSenders != null)
            {
                foreach (var provider in _alertSenders.Values)
                    provider?.Dispose();
            }
        }

        public class Input
        {
            public string AlertLevel { get; set; }
            public string AlertCode { get; set; }
            public string DefaultText { get; set; }
            public TextForConfig[] TextFor { get; set; } = { };

            public class TextForConfig
            {
                public string Handler { get; set; }
                public string Text { get; set; }
            }
        }
    }
}
