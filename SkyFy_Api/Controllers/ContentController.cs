using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using Renci.SshNet;
using SkyFy_Api.Models.Content;
using SkyFy_Api.Services;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace SkyFy_Api.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class ContentController : ControllerBase
    {
        private readonly DbService _dbService;
        private readonly SftpService _sftpService;
        private readonly IWebHostEnvironment _env;
        public ContentController(DbService dbService, SftpService Sftp, IWebHostEnvironment env)
        {
            _dbService = dbService;
            _sftpService = Sftp;
            _env = env;
        }


        [HttpGet("{id}/playlist.m3u8")]
        [Authorize]
        public async Task<IActionResult> GetPlaylist(long id)
        {
  
            if (!Request.Headers.Keys.Contains("skip_weather"))
            {
                var userIdString = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

                var weatherCode = Request.Headers.FirstOrDefault(x => x.Key == "weather_code").Value;
                if (string.IsNullOrEmpty(userIdString))
                    return BadRequest("Could not find User ID from token");

                if (string.IsNullOrEmpty(weatherCode))
                    return BadRequest("Weather code header missing");

                var stream = new Content_Stream_Create()
                {
                    Content_ID = id,
                    User_ID = long.Parse(userIdString),
                    WeatherCode = int.Parse(weatherCode)
                };

                _dbService.CreateEntity(stream, "Streams");

            }

            using var client = _sftpService.GetClient();
            client.Connect();

            string remotePlaylist = $"{id}/hls/playlist.m3u8";

            if (!client.Exists(remotePlaylist))
            {
                client.Disconnect();
                return NotFound("HLS playlist not found. Upload not processed?");
            }

            string tempPlaylist = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.m3u8");
            using (var fs = System.IO.File.Create(tempPlaylist))
                client.DownloadFile(remotePlaylist, fs);

            client.Disconnect();

            var lines = await System.IO.File.ReadAllLinesAsync(tempPlaylist);


            var baseUrl = $"{Request.Scheme}://{Request.Host}";


            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();

                if (line.EndsWith(".ts"))
                {

                    lines[i] = $"{baseUrl}/Content/{id}/hls/{line}";
                }
            }

            var updatedPlaylist = string.Join("\n", lines);

            System.IO.File.Delete(tempPlaylist);

            return Content(updatedPlaylist, "application/vnd.apple.mpegurl");
        }


        [HttpGet("{id}/hls/{segment}")]

        public IActionResult GetSegment(long id, string segment)
        {
            using var client = _sftpService.GetClient();
            client.Connect();

            string remoteFile = $"{id}/hls/{segment}";

            if (!client.Exists(remoteFile))
            {
                client.Disconnect();
                return NotFound();
            }

            string tempTs = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.ts");
            using (var fs = System.IO.File.Create(tempTs))
                client.DownloadFile(remoteFile, fs);

            client.Disconnect();

            var stream = System.IO.File.OpenRead(tempTs);
            Response.Headers["Content-Type"] = "video/mp2t";

            return new FileCallbackResult("video/mp2t", async (output, _) =>
            {
                await stream.CopyToAsync(output);
                stream.Dispose();
                System.IO.File.Delete(tempTs);
            });
        }
      


        [HttpPut("{id}/upload")]
        public async Task<IActionResult> Upload(long id, IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest("No file");


            var tempMp3 = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.mp3");
            await using (var fs = System.IO.File.Create(tempMp3))
                await file.CopyToAsync(fs);

            var tempHlsDir = Path.Combine(Path.GetTempPath(), $"hls_{id}_{Guid.NewGuid()}");
            Directory.CreateDirectory(tempHlsDir);

            await HlsConverter.ConvertMp3ToHlsAsync(tempMp3, tempHlsDir, segmentSeconds: 5);

 
            string remoteDir = $"{id}/hls";

            using (var client = _sftpService.GetClient())
            {
                client.Connect();

                _sftpService.EnsureDirectoryExists(client, remoteDir);

                foreach (var f in Directory.GetFiles(tempHlsDir))
                {
                    var fileName = Path.GetFileName(f);
                    using var localStream = System.IO.File.OpenRead(f);
                    var remoteFilePath = $"{remoteDir}/{fileName}";

                    client.UploadFile(localStream, remoteFilePath, true);
                }

                client.Disconnect();
            }

            System.IO.File.Delete(tempMp3);
            Directory.Delete(tempHlsDir, true);

            var playlistUrl = $"/Content/{id}/playlist.m3u8";
            return Ok(new { playlist = playlistUrl });
        }




        [HttpPost]
        public IActionResult Create([FromBody] ContentCreateRequest content)
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

            var contentEnd = new ContentBaseRequest()
            {
                Name = content.Name,
                User_ID = long.Parse(userId),
            };
            string ID = _dbService.CreateEntity(contentEnd, "Content");

            _sftpService.CreateFolder(ID);         

            return Ok(new { Message = "Created sucessfully", ID });
        }

        [HttpGet("all")]
        public IActionResult GetAll()
        {
            var data = _dbService.GetData<ContentClass>("Content");


            return Ok(data);
        }

        [HttpDelete("{id}")]
        [Authorize]
        public IActionResult Delete(long id) { 

            var content = _dbService.GetEntityById<ContentClass>(id, "Content");

            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

            if(string.IsNullOrEmpty(userId) || content == null)
            {
                return NotFound();
            }
            else if(content.User_ID.ToString() != userId)
            {
                return Unauthorized();
            }

            _sftpService.DeleteFolder(id.ToString());

            return Ok(_dbService.DeleteEntity(id, "Content"));
        }

        [HttpPut]
        [Authorize]
        public IActionResult UpdateMetaData([FromBody] ContentClass content)
        {
            return Ok(content/*.Update()*/);
        }

        [HttpGet("Search/{name}")]
        public async Task<IActionResult> Search(string name)
        {
            const string sql = @"
                                SELECT ""ID"", ""Name"", ""User_ID""
                                FROM ""Content""
                                WHERE ""Name"" ILIKE '%' || @searchName || '%'
                                LIMIT 20;
                            ";

            using (var conn = _dbService.GetConnection())
            {
                var contentList = new List<ContentClass>();
                await conn.OpenAsync();

                await using var cmd = new NpgsqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@searchName", name);

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    contentList.Add(new ContentClass
                    {
                        ID = reader.GetInt64(0),
                        Name = reader.GetString(1),
                        User_ID = reader.GetInt64(2)
                    });
                }

                return Ok(contentList);
            }
        }


        [HttpGet("weather/{weatherCode}/{limit}")]
        public async Task<IActionResult> GetTopContentByWeather(int weatherCode, int limit)
        {
            var contentList = new List<ContentClass>();

            const string sql = @"
                SELECT 
                    c.""ID"", 
                    c.""Name"", 
                    c.""User_ID"", 
                    COUNT(s.""ID"") AS stream_count
                FROM ""Streams"" s
                JOIN ""Content"" c ON c.""ID"" = s.""Content_ID""
                WHERE s.""WeatherCode"" = @weatherCode
                  AND s.""Stream_Date""::date = CURRENT_DATE
                GROUP BY c.""ID"", c.""Name"", c.""User_ID""
                ORDER BY stream_count DESC
                LIMIT @limit;
                ";

            using (var conn = _dbService.GetConnection())
            {
                await conn.OpenAsync();

                await using var cmd = new NpgsqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@weatherCode", weatherCode);
                cmd.Parameters.AddWithValue("@limit", limit);
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    var item = new ContentClass
                    {
                        ID = reader.GetInt64(0),
                        Name = reader.GetString(1),
                        User_ID = reader.GetInt64(2)
                    };

                    contentList.Add(item);
                }

                return Ok(contentList);
            }

        
        }

    }

    public class ContentClass : ContentBaseRequest
    {
        public long ID { get; set; }

    }

    public class ContentMediaClass : ContentClass
    {
        public byte[] File { get; set; }
        public string FileExtension { get; set; } = "mp3";
    }

    public class ContentCreateRequest
    {
        public string Name { get; set; }
    }

    public class ContentBaseRequest
    {
        public long id;
        public string Name { get; set; }
        //public string Cover_Art { get; set; }
        public long User_ID { get; set; }
        //public string Artist_ID { get; set; }
        //public string Data { get; set; }
    }
}
