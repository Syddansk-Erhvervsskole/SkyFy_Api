using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using Org.BouncyCastle.Asn1.Ocsp;
using Renci.SshNet;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;
using SkyFy_Api.Models.Content;
using SkyFy_Api.Services;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using static System.Net.Mime.MediaTypeNames;

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


        [HttpGet("{id}/Cover")]
        public async Task<IActionResult> GetCover(long id)
        {
            try
            {
                using var client = _sftpService.GetClient();
                client.Connect();

                string remotePath = $"{id}/cover.jpg";

                if (!client.Exists(remotePath))
                {
                    client.Disconnect();
                    return NotFound("Cover not found");
                }

                using var ms = new MemoryStream();
                client.DownloadFile(remotePath, ms);
                client.Disconnect();

                var fileBytes = ms.ToArray();
                return File(fileBytes, "image/jpeg");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error downloading cover: {ex.Message}");
            }
        }

        [HttpGet("{id}/{weatherCode}/playlist.m3u8")]
        [Authorize]
        public async Task<IActionResult> GetPlaylist(long id, string weatherCode)
        {
            if (!String.IsNullOrEmpty(weatherCode))
            {
                var userIdString = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

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

        [HttpPut("{id}/upload/cover")]
        public async Task<IActionResult> UploadCover(long id, [FromBody] IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest("No file");
        
            
            _sftpService.UploadFile(file, $"{id}/cover.jpg");

            return Ok();
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


        //Call for uploading content with all attachments
        [HttpPost("upload/all")]
        [Authorize]
        [ApiExplorerSettings(IgnoreApi = true)]
        [RequestSizeLimit(500_000_000)]
        public async Task<IActionResult> UploadAll(
    [FromForm] string name,
    [FromForm] IFormFile song,
    [FromForm] IFormFile cover)
        {
            var userId = long.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);

            using var conn = _dbService.GetConnection();
            await conn.OpenAsync();
            using var tx = conn.BeginTransaction();

            string contentId = string.Empty;

            try
            {
      
                var sql = @"INSERT INTO ""Content"" (""Name"", ""User_ID"") VALUES (@name, @userId) RETURNING ""ID"";";
                using (var cmd = new NpgsqlCommand(sql, conn, tx))
                {
                    cmd.Parameters.AddWithValue("@name", name);
                    cmd.Parameters.AddWithValue("@userId", userId);
                    contentId = (await cmd.ExecuteScalarAsync())!.ToString();
                }

                _sftpService.CreateFolder(contentId);

       
                if (cover == null || cover.Length == 0)
                    throw new Exception("Cover image is empty.");

                if (!cover.FileName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) &&
                    !cover.FileName.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
                {
                    throw new Exception("Only JPG files are allowed.");
                }


                await using var uploadStream = cover.OpenReadStream();
                using var img = await SixLabors.ImageSharp.Image.LoadAsync(uploadStream);

                const int maxSize = 250;

                if (img.Width > maxSize || img.Height > maxSize)
                {
                    double scale = Math.Min((double)maxSize / img.Width, (double)maxSize / img.Height);
                    int newW = (int)Math.Round(img.Width * scale);
                    int newH = (int)Math.Round(img.Height * scale);

                    img.Mutate(x => x.Resize(new ResizeOptions
                    {
                        Mode = ResizeMode.Max,
                        Size = new Size(newW, newH)
                    }));
                }

                using var ms = new MemoryStream();
                await img.SaveAsJpegAsync(ms, new JpegEncoder
                {
                    Quality = 75 
                });
                ms.Position = 0;

                _sftpService.UploadFile(ms, $"{contentId}/cover.jpg");

                var tempMp3 = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.mp3");
                await using (var fs = System.IO.File.Create(tempMp3))
                    await song.CopyToAsync(fs);

                var tempHlsDir = Path.Combine(Path.GetTempPath(), $"hls_{contentId}_{Guid.NewGuid()}");
                Directory.CreateDirectory(tempHlsDir);

                await HlsConverter.ConvertMp3ToHlsAsync(tempMp3, tempHlsDir);

                using (var client = _sftpService.GetClient())
                {
                    client.Connect();
                    string remoteDir = $"{contentId}/hls";
                    _sftpService.EnsureDirectoryExists(client, remoteDir);

                    foreach (var f in Directory.GetFiles(tempHlsDir))
                    {
                        using var local = System.IO.File.OpenRead(f);
                        client.UploadFile(local, $"{remoteDir}/{Path.GetFileName(f)}");
                    }

                    client.Disconnect();
                }

                System.IO.File.Delete(tempMp3);
                Directory.Delete(tempHlsDir, true);

                await tx.CommitAsync();

                return Ok(new { id = contentId, message = "Uploaded successfully" });
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                try { _sftpService.DeleteFolder(contentId); } catch { }
                return BadRequest(new { message = ex.Message });
            }
        }



        [HttpGet("all")]
        public IActionResult GetAll()
        {
            var data = _dbService.GetData<ContentClass>("Content");
            return Ok(data);
        }

        [HttpDelete("{id}")]
        [Authorize]
        public IActionResult Delete(long id)
        {

            var content = _dbService.GetEntityById<ContentClass>(id, "Content");

            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

            if (string.IsNullOrEmpty(userId) || content == null)
            {
                return NotFound();
            }
            else if (content.User_ID.ToString() != userId)
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
            //const string sql = @"
            //                    SELECT ""ID"", ""Name"", ""User_ID""
            //                    FROM ""Content""
            //                    WHERE ""Name"" ILIKE '%' || @searchName || '%'
            //                    LIMIT 20;
            //                ";

            const string sql = @"
                SELECT 
                    c.""ID"", 
                    c.""Name"", 
                    c.""User_ID"",
                    CASE 
                        WHEN lc.""ID"" IS NOT NULL THEN TRUE 
                        ELSE FALSE 
                    END AS is_liked
                FROM ""Content"" c
                LEFT JOIN ""LikedContent"" lc 
                    ON lc.""Content_ID"" = c.""ID"" AND lc.""User_ID"" = c.""User_ID""
                WHERE c.""Name"" ILIKE '%' || @searchName || '%'
                LIMIT 20;
            ";

            using (var conn = _dbService.GetConnection())
            {
                var contentList = new List<ContentFinalClass>();
                await conn.OpenAsync();

                await using var cmd = new NpgsqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@searchName", name);

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    contentList.Add(new ContentFinalClass
                    {
                        ID = reader.GetInt64(0),
                        Name = reader.GetString(1),
                        User_ID = reader.GetInt64(2),
                        Cover_Art = $"{Request.Scheme}://{Request.Host}/Content/{reader.GetInt64(0)}/Cover",
                        Liked = reader.GetBoolean(3)
                    });
                }

                return Ok(contentList);
            }
        }

        [HttpGet("weather/{weatherCode}/{limit}")]
        public async Task<IActionResult> GetTopContentByWeather(int weatherCode, int limit)
        {
            var contentList = new List<ContentFinalClass>();

            const string sql = @"
                SELECT 
                    c.""ID"", 
                    c.""Name"", 
                    c.""User_ID"", 
                    COUNT(s.""ID"") AS stream_count,
                    CASE 
                        WHEN lc.""ID"" IS NOT NULL THEN TRUE 
                        ELSE FALSE 
                    END AS is_liked
                FROM ""Streams"" s
                JOIN ""Content"" c ON c.""ID"" = s.""Content_ID""
                LEFT JOIN ""LikedContent"" lc 
                    ON lc.""Content_ID"" = c.""ID"" AND lc.""User_ID"" = s.""User_ID""
                WHERE s.""WeatherCode"" = @weatherCode
                    AND s.""Stream_Date""::date = CURRENT_DATE
                GROUP BY c.""ID"", c.""Name"", c.""User_ID"", is_liked
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
                    var item = new ContentFinalClass
                    {
                        ID = reader.GetInt64(0),
                        Name = reader.GetString(1),
                        User_ID = reader.GetInt64(2),
                        Cover_Art = $"{Request.Scheme}://{Request.Host}/Content/{reader.GetInt64(0)}/Cover",
                        Liked = reader.GetBoolean(4)
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

        public ContentFinalClass ConvertToFinalClass(HttpRequest request)
        {
            var finalClass = new ContentFinalClass();
            finalClass.ID = this.ID;
            finalClass.Name = this.Name;
            finalClass.User_ID = this.User_ID;
            finalClass.Cover_Art = $"{request.Scheme}://{request.Host}/Content/{ID}/Cover";
            return finalClass;
        }
    }

    public class ContentFinalClass : ContentBaseRequest
    {
        public long ID { get; set; }
        public string Cover_Art { get; set; }
        public bool Liked { get; set; }
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
