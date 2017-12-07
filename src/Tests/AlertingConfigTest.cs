using Newtonsoft.Json;
using SuperSimpleAlertsNet;
using Xunit;

namespace Tests
{
    public class AlertingConfigTest
    {
        [Fact]
        public void GetMatchingHandlers_Test()
        {
            var config = new AlertingConfig
            {
                Contacts = new[]
                {
                    new AlertingConfig.ContactsConfig
                    {
                        Name = "Tom",
                        EndPoints = new[]
                        {
                            new AlertingConfig.ContactsConfig.EndPointsConfig
                            {
                                Value = "tom@gmail.com",
                                Type = "email"
                            },
                            new AlertingConfig.ContactsConfig.EndPointsConfig
                            {
                                Value = "0411111111",
                                Type = "sms"
                            },
                        }
                    },
                    new AlertingConfig.ContactsConfig
                    {
                        Name = "Harry",
                        EndPoints = new[]
                        {
                            new AlertingConfig.ContactsConfig.EndPointsConfig
                            {
                                Value = "harry@gmail.com",
                                Type = "email"
                            },
                            new AlertingConfig.ContactsConfig.EndPointsConfig
                            {
                                Value = "0422222222",
                                Type = "sms"
                            },
                        }
                    },
                    new AlertingConfig.ContactsConfig
                    {
                        Name = "SlackBoard",
                        EndPoints = new[]
                        {
                            new AlertingConfig.ContactsConfig.EndPointsConfig
                            {
                                Value = "http://slackboard.com",
                                Type = "slack"
                            },
                        }
                    }
                },
                HandlerGroups = new[]
                {
                    new AlertingConfig.HandlerGroupConfig
                    {
                        Code = "EverybodyEmail",
                        Handlers = new[]
                        {
                            new AlertingConfig.HandlerConfig
                            {
                                Handler = "email",
                                Contacts = new[] {"Tom", "Harry"},
                            },
                        }
                    },
                    new AlertingConfig.HandlerGroupConfig
                    {
                        Code = "EverybodySms",
                        Handlers = new[]
                        {
                            new AlertingConfig.HandlerConfig
                            {
                                Handler = "sms",
                                Contacts = new[] {"Tom", "Harry"},
                            },
                        }
                    },
                },
                Subscriptions = new[]
                {
                    new AlertingConfig.SubscriptionsConfig
                    {
                        AlertCode = "Play completed",
                        HandlerGroups = new[]
                        {
                            "EverybodyEmail",
                        },
                        Handlers = new[]
                        {
                            new AlertingConfig.HandlerConfig
                            {
                                Handler = "slack",
                                Contacts = new[]
                                {
                                    "slackBoard",
                                },
                                Coloring = "Red"
                            },
                        }
                    },
                    new AlertingConfig.SubscriptionsConfig
                    {
                        HandlerGroups = new[]
                        {
                            "EverybodyEmail",
                        },
                        Handlers = new[]
                        {
                            new AlertingConfig.HandlerConfig
                            {
                                Handler = "slack",
                                Contacts = new[]
                                {
                                    "slackBoard",
                                }
                            },
                        }
                    },
                    new AlertingConfig.SubscriptionsConfig
                    {
                        AlertLevel = "Critical",
                        HandlerGroups = new[]
                        {
                            "EverybodyEmail",
                            "EverybodySms",
                        },
                        Handlers = new[]
                        {
                            new AlertingConfig.HandlerConfig
                            {
                                Handler = "slack",
                                Contacts = new[]
                                {
                                    "slackBoard",
                                }
                            },
                        }
                    },
                }
            };

            var criticalHandlers = config.GetMatchingHandlers("PlayDied", "Critical");
            Assert.Equal(3, criticalHandlers.Count);
            Assert.Contains(criticalHandlers, handlerConfig => handlerConfig.Handler == "slack");
            Assert.Contains(criticalHandlers, handlerConfig => handlerConfig.Handler == "sms" && handlerConfig.Contacts.Length == 2);
            Assert.Contains(criticalHandlers, handlerConfig => handlerConfig.Handler == "email" && handlerConfig.Contacts.Length == 2);

            var playCompletedHandlers = config.GetMatchingHandlers("Play Completed", "InfoAlert");
            Assert.Equal(2, playCompletedHandlers.Count);
            Assert.Contains(playCompletedHandlers, handlerConfig => handlerConfig.Handler == "slack" && handlerConfig.Coloring == "Red");
            Assert.Contains(playCompletedHandlers, handlerConfig => handlerConfig.Handler == "email" && handlerConfig.Contacts.Length == 2);

            var warningHandlers = config.GetMatchingHandlers("Play Didn't Buy", "Warning");
            Assert.Equal(2, warningHandlers.Count);
            Assert.Contains(warningHandlers, handlerConfig => handlerConfig.Handler == "slack" && string.IsNullOrEmpty(handlerConfig.Coloring));
            Assert.Contains(warningHandlers, handlerConfig => handlerConfig.Handler == "email" && handlerConfig.Contacts.Length == 2);
        }

        [Fact]
        public void Deserialization_Test()
        {
            var json = @"
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

            var alertConfig = JsonConvert.DeserializeObject<AlertingConfig>(json);

            Assert.Equal(2, alertConfig.Subscriptions.Length);
            Assert.Equal(2, alertConfig.Contacts.Length);
            Assert.Equal("critical", alertConfig.Subscriptions[0].AlertLevel);
            Assert.Equal(2, alertConfig.Subscriptions[0].Handlers.Length);
        }
    }
}
