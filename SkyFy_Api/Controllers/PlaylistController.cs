using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using SkyFy_Api.Services;
using System.Data.Common;

namespace SkyFy_Api.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class PlaylistController : ControllerBase
    {
        private readonly DbService _dbService;

        public PlaylistController(DbService dbService)
        {
            _dbService = dbService;
        }

        [HttpGet("Content/{id}")]
        [Authorize]
        public async Task<IActionResult> GetPlaylistContent(long id)
        {
            try
            {
                var userId = long.Parse(RequestHelper.GetUserIDFromClaims(User));

                const string sql = @"
            SELECT 
                c.""ID"",
                c.""Name"",
                c.""User_ID"",
                CASE 
                    WHEN lc.""ID"" IS NOT NULL THEN TRUE 
                    ELSE FALSE 
                END AS is_liked
            FROM ""PlaylistContent"" pc
            JOIN ""Content"" c 
                ON c.""ID"" = pc.""Content_ID""
            LEFT JOIN ""LikedContent"" lc 
                ON lc.""Content_ID"" = c.""ID"" 
               AND lc.""User_ID"" = @userId
            WHERE pc.""Playlist_ID"" = @playlistId;
        ";

                using var conn = _dbService.GetConnection();
                await conn.OpenAsync();

                var songs = new List<ContentFinalClass>();

                await using var cmd = new NpgsqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@playlistId", id);
                cmd.Parameters.AddWithValue("@userId", userId);

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    songs.Add(new ContentFinalClass
                    {
                        ID = reader.GetInt64(0),
                        Name = reader.GetString(1),
                        User_ID = reader.GetInt64(2),
                        Cover_Art = $"{Request.Scheme}://{Request.Host}/Content/{reader.GetInt64(0)}/Cover",
                        Liked = reader.GetBoolean(3)
                    });
                }

                return Ok(songs);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }


        [HttpGet("ById/{id}")]
        [Authorize]
        public IActionResult GetPlaylistById(long id)
        {
            var userId = RequestHelper.GetUserIDFromClaims(User);
            try
            {
                var playlist = _dbService.GetEntityById<Playlist>(id, "Playlists");

                if (playlist == null)
                {
                    return NotFound(new { message = "Playlist not found" });
                }
                return Ok(playlist);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpGet("all")]
        [Authorize]
        public IActionResult GetPlaylists()
        {
            var userId = RequestHelper.GetUserIDFromClaims(User);
            try
            {
                var playlist = _dbService.GetEntitiesByField<Playlist>("User_ID", long.Parse(userId), "Playlists");

                if (playlist == null)
                {
                    return NotFound(new { message = "Playlist not found" });
                }
                return Ok(playlist);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("Create/{name}")]
        [Authorize]
        public IActionResult CreatePlaylist(string name)
        {
            var userId = RequestHelper.GetUserIDFromClaims(User);
            try
            {
                var playlist = new PlaylistCreate()
                {
                    Name = name,
                    User_ID = long.Parse(userId),
                };
                _dbService.CreateEntity(playlist, "Playlists");

                return Ok(playlist);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }


        [HttpPost("{playlist_id}/add/content/{content_id}")]
        [Authorize]
        public IActionResult PlaylistAddContent(long playlist_id, long content_id)
        {
            var userId = RequestHelper.GetUserIDFromClaims(User);
            try
            {
                var content = new Playlist_Content
                {
                    Playlist_ID = playlist_id,
                    Content_ID = content_id
                };

                _dbService.CreateEntity(content, "PlaylistContent");

                return Ok(content);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }


        [HttpDelete("{playlist_id}/remove/content/{content_id}")]
        [Authorize]
        public IActionResult PlaylistRemoveContent(long playlist_id, long content_id)
        {
            var userId = long.Parse(RequestHelper.GetUserIDFromClaims(User));

            try
            {
                const string sql = @"
            DELETE FROM ""PlaylistContent""
            WHERE ""Playlist_ID"" = @playlist_id
              AND ""Content_ID"" = @content_id;
        ";

                using var conn = _dbService.GetConnection();
                conn.Open();

                using var cmd = new NpgsqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@playlist_id", playlist_id);
                cmd.Parameters.AddWithValue("@content_id", content_id);

                int rows = cmd.ExecuteNonQuery();

                if (rows == 0)
                    return NotFound(new { message = "Song not in playlist" });

                return Ok(new { message = "Song removed from playlist" });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }


    }

    public class Playlist
    {
        public long ID { get; set; }
        public string Name { get; set; }
        public long User_ID { get; set; }
       
    }



    public class PlaylistCreate
    {
        public string Name { get; set; }

        public long User_ID { get; set; }
    }

    public class Playlist_Content
    {
        public long ID { get; set; }
        public long Playlist_ID { get; set; }
        public long Content_ID { get; set; }
    }



}
