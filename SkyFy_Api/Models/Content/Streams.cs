namespace SkyFy_Api.Models.Content
{
    public class Content_Stream
    {
        public long ID { get; set; }
        public long Content_ID { get; set; }
        public long User_ID { get; set; }
        public int WeatherCode { get; set; }
        public DateTime Stream_Date { get; set; }
    }

    public class Content_Stream_Create
    {
        public long Content_ID { get; set; }
        public long User_ID { get; set; }
        public int WeatherCode { get; set; }
    }
}
