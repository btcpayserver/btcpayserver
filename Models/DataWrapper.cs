using Newtonsoft.Json;

namespace BTCPayServer.Models
{
    public class DataWrapper
    {
        public static DataWrapper<T> Create<T>(T obj)
        {
            return new DataWrapper<T>(obj);
        }
    }
    public class DataWrapper<T>
    {
        public DataWrapper()
        {

        }
        public DataWrapper(T data)
        {
            Data = data;
        }

        [JsonProperty("facade", NullValueHandling = NullValueHandling.Ignore)]
        public string Facade
        {
            get; set;
        }
        [JsonProperty("data")]
        public T Data
        {
            get; set;
        }
    }
}
