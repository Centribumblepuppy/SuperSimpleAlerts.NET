using System;
using System.Collections;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Redis;

namespace SuperSimpleAlertsNet.Statefulness
{
    public class RedisStatefulness : IStatefulness
    {
        private readonly RedisCache _redisCache;

        public RedisStatefulness(IDictionary environmentVariables)
        {
            var redisConfigurationString = (string)environmentVariables["redisConfigString"];

            if (string.IsNullOrEmpty(redisConfigurationString))
                throw new InvalidOperationException("redisConfigurationString not found in environment variables");
            
            _redisCache = new RedisCache(new RedisCacheOptions
            {
                Configuration = redisConfigurationString
            });
        }

        public bool ShouldDeduplicate(string alertCode)
        {
            var cacheKey = GetCacheKey(alertCode);
            return !string.IsNullOrEmpty(_redisCache.GetString(cacheKey));
        }

        private static string GetCacheKey(string alertCode)
        {
            var cacheKey = "alert-sent-" + alertCode;
            return cacheKey;
        }

        public void SetAlertWasSent(string alertCode, TimeSpan deduplicationPeriod)
        {
            var cacheKey = GetCacheKey(alertCode);

            _redisCache.SetString(cacheKey, "sent",
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = deduplicationPeriod
                });
        }
    }
}
