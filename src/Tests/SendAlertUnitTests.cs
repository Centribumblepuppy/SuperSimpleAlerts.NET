using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using Amazon.Lambda.Core;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NSubstitute;
using SuperSimpleAlertsNet;
using SuperSimpleAlertsNet.Statefulness;
using Xunit;

namespace Tests
{
    public class SendAlertUnitTests
    {
        [Fact]
        public async Task Test_WhenASpecificHandlerForTheAlertIsConfigured_ShouldSendOnlyToThatHandler()
        {
            var emailProvider = Substitute.For<IAlertSender>();
            var smsProvider = Substitute.For<IAlertSender>();

            var providers = new Dictionary<string, IAlertSender>
            {
                {"email", emailProvider},
                {"sms", smsProvider}
            };

            var config = GetConfig();

            var s3 = MockS3Client(config);

            var environmentVariables = new ListDictionary
            {
                {"configurationFileS3Bucket", "myBucket"},
                {"configurationFileS3Key", "myKey"}
            };
            
            var statefulness = Substitute.For<IStatefulness>();

            var sendAlert = new SendAlert(
                s3,
                environmentVariables,
                providers,
                statefulness);

            var lambdaContext = Substitute.For<ILambdaContext>();

            // ACT
            var inputJson = JObject.Parse(@"
{
""alertLevel"": ""critical"",
""alertCode"": ""somethingReallyWrong"",
""defaultText"": ""Help!!!"",
""textFor"": [
    {
        ""handler"": ""sms"",
        ""text"": ""Get me out of here!!!""
    }
]
}");
            var input = JsonConvert.DeserializeObject<SendAlert.Input>(inputJson.ToString());
            
            // ACT
            var result = await sendAlert.Send(input, lambdaContext);

            // ASSERT
            Assert.Equal(200, result.StatusCode);

            // Shouldn't have sent any emails
            await emailProvider
                .Received(0)
                .Send(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ILambdaLogger>());

            // Should have sent SMS to Harry
            await smsProvider
                .Received(1)
                .Send(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ILambdaLogger>());
        }

        [Fact]
        public async Task Test_WhenOnlyDefaultHandlersForTheAlertAreConfigured()
        {
            var emailProvider = Substitute.For<IAlertSender>();
            var smsProvider = Substitute.For<IAlertSender>();

            var providers = new Dictionary<string, IAlertSender>
            {
                {"email", emailProvider},
                {"sms", smsProvider}
            };

            var config = GetConfig();

            var s3 = MockS3Client(config);

            var environmentVariables = new ListDictionary
            {
                {"configurationFileS3Bucket", "myBucket"},
                {"configurationFileS3Key", "myKey"}
            };
            
            var statefulness = Substitute.For<IStatefulness>();

            var sendAlert = new SendAlert(
                s3,
                environmentVariables,
                providers,
                statefulness);

            var lambdaContext = Substitute.For<ILambdaContext>();

            // ACT
            var inputJson = JObject.Parse(@"
{
""alertLevel"": ""critical"",
""alertCode"": ""thisIsTerrible"",
""defaultText"": ""Real bad!!!"",
""textFor"": [
    {
        ""handler"": ""sms"",
        ""text"": ""Arrggg!!!""
    }
]
}");

            var input = JsonConvert.DeserializeObject<SendAlert.Input>(inputJson.ToString());
            var result = await sendAlert.Send(input, lambdaContext);

            Assert.Equal(200, result.StatusCode);

            // Should have sent emails to Tom and Harry
            await emailProvider
                .Received(2)
                .Send(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ILambdaLogger>());

            // Should have sent SMSs to Tom and Harry
            await smsProvider
                .Received(2)
                .Send(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ILambdaLogger>());
        }

        [Fact]
        public async Task TestDeduplication_WithRedisIntegration()
        {
            var emailProvider = Substitute.For<IAlertSender>();
            
            var providers = new Dictionary<string, IAlertSender>
            {
                {"email", emailProvider},
            };

            var config = GetConfigWithDeduplication();

            var s3 = MockS3Client(config);

            var environmentVariables = new ListDictionary
            {
                {"configurationFileS3Bucket", "myBucket"},
                {"configurationFileS3Key", "myKey"},
                {"redisConfigString", "localhost:6379,syncTimeout=3000"}
            };
            
            var statefulness = new RedisStatefulness(environmentVariables);

            var sendAlert = new SendAlert(
                s3,
                environmentVariables,
                providers,
                statefulness);

            var lambdaContext = Substitute.For<ILambdaContext>();

            // ACT
            var nonDeduplicatedAlertJson = JObject.Parse(@"
{
""alertLevel"": ""critical"",
""alertCode"": ""thisIsTerrible"",
""defaultText"": ""Real bad!!!""
}");

            var deduplicatedAlertJson = JObject.Parse(@"
{
""alertLevel"": ""error"",
""alertCode"": ""somethingIsWrong"",
""defaultText"": ""Not really so bad...""
}");

            var nonDeduplicatedAlert = JsonConvert.DeserializeObject<SendAlert.Input>(nonDeduplicatedAlertJson.ToString());
            var deduplicatedAlert = JsonConvert.DeserializeObject<SendAlert.Input>(deduplicatedAlertJson.ToString());
            await sendAlert.Send(nonDeduplicatedAlert, lambdaContext);
            await sendAlert.Send(nonDeduplicatedAlert, lambdaContext);
            await sendAlert.Send(deduplicatedAlert, lambdaContext);
            await sendAlert.Send(deduplicatedAlert, lambdaContext);
            
            // Should have sent any emails
            await emailProvider
                .Received(2)
                .Send(Arg.Any<string>(), Arg.Any<string>(), Arg.Is("thisIsTerrible"), Arg.Any<string>(), Arg.Any<ILambdaLogger>());
            await emailProvider
                .Received(1)
                .Send(Arg.Any<string>(), Arg.Any<string>(), Arg.Is("somethingIsWrong"), Arg.Any<string>(), Arg.Any<ILambdaLogger>());
        }

        private static IAmazonS3 MockS3Client(string config)
        {
            var s3 = Substitute.For<IAmazonS3>();
            s3.GetObjectAsync("myBucket", "myKey").Returns(
                Task.FromResult(new GetObjectResponse
                {
                    ResponseStream = GenerateStreamFromString(config),
                    HttpStatusCode = HttpStatusCode.OK
                }));
            return s3;
        }

        private static string GetConfig()
        {
            return @"
{
	""contacts"": [
		{
			""name"": ""Tom"",
			""endpoints"": [
				{
					""type"": ""sms"",
					""value"": ""+61411111111""
				},
				{
					""type"": ""email"",
					""value"": ""tom@gmail.com""
				}
			],
		},
		{
			""name"": ""Harry"",
			""endpoints"": [
				{
					""type"": ""sms"",
					""value"": ""+61400000000""
				},
                {
					""type"": ""email"",
					""value"": ""harry@gmail.com""
				}
			]
		}
	],
	""subscriptions"": [
		{
			""alertLevel"": ""critical"",
			""handlers"": [
				{
					""handler"": ""email"",
					""contacts"": [ ""Tom"", ""Harry"" ]
				},
                {
					""handler"": ""sms"",
					""contacts"": [ ""Tom"", ""Harry"" ]
				}
			]
		},
        {
			""alertCode"": ""somethingReallyWrong"",
			""handlers"": [
				{
					""handler"": ""sms"",
					""contacts"": [ ""Harry"" ]
				}
			]
		}
	]
}";
        }

        private static string GetConfigWithDeduplication()
        {
            return @"
{
	""contacts"": [
		{
			""name"": ""Tom"",
			""endpoints"": [
				{
					""type"": ""email"",
					""value"": ""tom@gmail.com""
				}
			],
		}
	],
	""subscriptions"": [
        {
	        ""handlers"": [
		        {
			        ""handler"": ""email"",
			        ""contacts"": [ ""Tom"" ]
		        }
	        ]
        }
    ],
	""deduplicate"": {
        ""enabled"":true,
        ""deduplicatePerSecond"": 0,
		""specifics"": [
			{
				""alertCode"": ""somethingIsWrong"",
				""deduplicatePerSecond"": 5
			}
		]
	}    
}";
        }

        public static Stream GenerateStreamFromString(string s)
        {
            MemoryStream stream = new MemoryStream();
            StreamWriter writer = new StreamWriter(stream);
            writer.Write(s);
            writer.Flush();
            stream.Position = 0;
            return stream;
        }
    }
}
