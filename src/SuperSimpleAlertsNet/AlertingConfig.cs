using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Newtonsoft.Json;

namespace SuperSimpleAlertsNet
{
    public class AlertingConfig
    {
        public SubscriptionsConfig[] Subscriptions { get; set; } = {};
        public HandlerGroupConfig[] HandlerGroups { get; set; } = {};
        public ContactsConfig[] Contacts { get; set; } = { };
        public DeduplicateConfig Deduplicate { get; set; } = new DeduplicateConfig();
        
        public void Validate()
        {
            if (Contacts == null)
                throw new InvalidConfigurationException($"Invalid configuration for '{nameof(Contacts)}' section.");
            if (Subscriptions == null)
                throw new InvalidConfigurationException($"Invalid configuration for '{nameof(Subscriptions)}' section.");
            if (HandlerGroups == null)
                throw new InvalidConfigurationException($"Invalid configuration for '{nameof(HandlerGroups)}' section.");
            if (Deduplicate == null)
                throw new InvalidConfigurationException($"Invalid configuration for '{nameof(Deduplicate)}' section.");

            if (Contacts.Any(x => x == null))
                throw new InvalidConfigurationException($"Invalid configuration for '{nameof(Contacts)}' section.");
            if (Subscriptions.Any(x => x == null))
                throw new InvalidConfigurationException($"Invalid configuration for '{nameof(Subscriptions)}' section.");
            if (HandlerGroups.Any(x => x == null))
                throw new InvalidConfigurationException($"Invalid configuration for '{nameof(HandlerGroups)}' section.");

            Deduplicate.Validate();
        }

        public IList<HandlerConfig> GetMatchingHandlers(
            string alertCode,
            string alertLevel)
        {
            var subscription = Subscriptions
                       ?.FirstOrDefault(x => !string.IsNullOrEmpty(x.AlertCode)
                                             && !string.IsNullOrEmpty(x.AlertLevel)
                                             && x.AlertCode.Equals(alertCode, StringComparison.OrdinalIgnoreCase)
                                             && x.AlertLevel.Equals(alertLevel, StringComparison.OrdinalIgnoreCase))
                   ?? Subscriptions
                       ?.FirstOrDefault(x => !string.IsNullOrEmpty(x.AlertCode)
                                             && string.IsNullOrEmpty(x.AlertLevel)
                                             && x.AlertCode.Equals(alertCode, StringComparison.OrdinalIgnoreCase))
                   ?? Subscriptions
                       ?.FirstOrDefault(x => !string.IsNullOrEmpty(x.AlertLevel)
                                             && string.IsNullOrEmpty(x.AlertCode)
                                             && x.AlertLevel.Equals(alertLevel, StringComparison.OrdinalIgnoreCase))
                   ?? Subscriptions
                       ?.FirstOrDefault(x => string.IsNullOrEmpty(x.AlertLevel)
                                             && string.IsNullOrEmpty(x.AlertCode));

            if (subscription == null) return new List<HandlerConfig>();

            var handlers = new List<HandlerConfig>(subscription.Handlers);

            if (subscription.HandlerGroups.Any())
            {
                var handlerGroups = HandlerGroups
                    .Where(x => subscription.HandlerGroups.Any(y => x.Code.Equals(y, StringComparison.OrdinalIgnoreCase)))
                    .SelectMany(x => x.Handlers);

                handlers.AddRange(handlerGroups);
            }

            return handlers;
        }

        public class SubscriptionsConfig
        {
            public string AlertLevel { get; set; }
            public string AlertCode { get; set; }
            public HandlerConfig[] Handlers { get; set; }
            public string[] HandlerGroups { get; set; } = { };
        }

        public class HandlerConfig
        {
            public string Handler { get; set; }
            public string[] Contacts { get; set; }
            public string Coloring { get; set; }
        }

        public class HandlerGroupConfig
        {
            public string Code { get; set; }
            public HandlerConfig[] Handlers { get; set; }
        }

        public class ContactsConfig
        {
            public string Name { get; set; }
            public EndPointsConfig[] EndPoints { get; set; } = { };

            public class EndPointsConfig
            {
                public string Type { get; set; }
                public string Value { get; set; }
            }
        }

        public class DeduplicateConfig
        {
            [Description("Whether or not to enable alerting via SMS, email, Slack etc")]
            [JsonProperty(Required=Required.Default)]
            [DefaultValue("true")]
            public bool Enabled { get; set; } = true;

            [Description("Alert messages with the same code can be de-duplicated if they appear frequently close together.")]
            [JsonProperty(Required=Required.Default)]
            [DefaultValue("30")]
            public int DeduplicatePerSecond { get; set; } = 30;

            [Description("More specific configuration settings regarding de-duplication.")]
            public DeduplicateSpecificsConfig[] Specifics { get; set; } = { };

            public DeduplicateSpecificsConfig GetSpecific(string alertCode, string alertLevel)
            {
                if (string.IsNullOrEmpty(alertCode))
                    throw new ArgumentNullException(nameof(alertCode));
                if (string.IsNullOrEmpty(alertLevel))
                    throw new ArgumentNullException(nameof(alertLevel));

                var oic = StringComparison.OrdinalIgnoreCase;

                return Specifics
                           .FirstOrDefault(x =>
                               !string.IsNullOrEmpty(x.AlertCode)
                               && !string.IsNullOrEmpty(x.AlertLevel)
                               && x.AlertCode.Equals(alertCode, oic)
                               && x.AlertLevel.Equals(alertLevel, oic))
                       ?? Specifics
                           .FirstOrDefault(x => !string.IsNullOrEmpty(x.AlertCode)
                                                && string.IsNullOrEmpty(x.AlertLevel)
                                                && x.AlertCode.Equals(alertCode, oic))
                       ?? Specifics
                           .FirstOrDefault(x => !string.IsNullOrEmpty(x.AlertLevel)
                                                && string.IsNullOrEmpty(x.AlertCode)
                                                && x.AlertLevel.Equals(alertLevel, oic));
            }

            public void Validate()
            {
                if (Specifics.Any(x => x == null))
                    throw new InvalidConfigurationException($"Invalid configuration for '{nameof(Specifics)}' section.");
            }
        }

        public class DeduplicateSpecificsConfig
        {
            [JsonProperty(Required=Required.Default)]
            public string AlertCode { get; set; }
            
            [JsonProperty(Required=Required.Default)]
            public string AlertLevel { get; set; }
            
            [JsonProperty(Required=Required.Always)]
            public int DeduplicatePerSecond { get; set; }
        }
    }
}