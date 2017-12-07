using System.IO;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;

namespace SuperSimpleAlertsNet
{
    public class JsonStringConfigurationSource: IConfigurationSource
    {
        private readonly string _json;

        public JsonStringConfigurationSource(string json)
        {
            _json = json;
        }

        public bool Optional { get; set; }
        public int ReloadDelay { get; set; }
        public bool ReloadOnChange { get; set; }

        public IConfigurationProvider Build(IConfigurationBuilder builder)
        {
            return new JsonStringConfigurationProvider(this, _json);
        }
    }

    public class JsonStringConfigurationProvider : JsonConfigurationProvider
    {
        private readonly string _json;

        public JsonStringConfigurationProvider(JsonStringConfigurationSource source, string json)
            : base(new JsonConfigurationSource{
                Optional = source.Optional,
                ReloadOnChange = source.ReloadOnChange
                })
        {
            _json = json;
        }

        public override void Load()
        {
            using (var memoryStream = new MemoryStream())
            using (var writer = new StreamWriter(memoryStream))
            {
                writer.Write(_json);
                writer.Flush();
                memoryStream.Position = 0;
                base.Load(memoryStream);
            }
        }
    }
}
