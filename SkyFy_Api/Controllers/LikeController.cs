using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using SkyFy_Api.Services;

namespace SkyFy_Api.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class LikeController : ControllerBase
    {
        private readonly DbService _dbService;
        public LikeController(DbService dbService)
        {
            _dbService = dbService;
        }

        [HttpGet()]
        [Authorize]
        public async Task<IActionResult> GetLikes()
        {
            try
            {
                var userId = long.Parse(RequestHelper.GetUserIDFromClaims(User));

                const string sql = @"
                    SELECT 
                        c.""ID"", 
                        c.""Name"",
                        c.""User_ID"",
                        TRUE AS is_liked
                    FROM ""LikedContent"" lc
                    JOIN ""Content"" c 
                        ON c.""ID"" = lc.""Content_ID""
                    WHERE lc.""User_ID"" = @userId;
                ";

                using var conn = _dbService.GetConnection();
                await conn.OpenAsync();

                var likedSongs = new List<ContentFinalClass>();

                await using var cmd = new NpgsqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@userId", userId);

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    likedSongs.Add(new ContentFinalClass
                    {
                        ID = reader.GetInt64(0),
                        Name = reader.GetString(1),
                        User_ID = reader.GetInt64(2),
                        Cover_Art = $"{Request.Scheme}://{Request.Host}/Content/{reader.GetInt64(0)}/Cover",
                        Liked = reader.GetBoolean(3) // always true
                    });
                }

                return Ok(likedSongs);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }


        [HttpPost("{id}")]
        [Authorize]
        public IActionResult Like(long id)
        {
            try
            {
                var userId = long.Parse(RequestHelper.GetUserIDFromClaims(User));

                var likes = _dbService.GetEntitiesByField<Like>("User_ID", userId, "LikedContent");

                if (likes.Any(x => x.Content_ID == id))
                {
                    return BadRequest(new { message = "User already liked content" });
                }

                var like = new LikeCreateRequest()
                {
                    User_ID = userId,
                    Content_ID = id
                };

                _dbService.CreateEntity(like, "LikedContent");

                return Ok(likes);
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpDelete("{id}")]
        [Authorize]
        public IActionResult Unlike(long id)
        {
            try
            {
                var userId = long.Parse(RequestHelper.GetUserIDFromClaims(User));
                var like = _dbService.GetEntityByField<Like>("Content_ID", id, "LikedContent");
                if (like == null || like.User_ID != userId)
                {
                    return NotFound(new { message = "Like not found" });
                }
                _dbService.DeleteEntity(like.ID, "LikedContent");
                return Ok(new { message = "Like removed successfully" });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
    }

    public class Like
    {
        public long ID { get; set; }
        public long User_ID { get; set; }
        public long Content_ID { get; set; }
        public DateTime Liked_Date { get; set; }
    }

    public class LikeCreateRequest
    {
        public long User_ID { get; set; }
        public long Content_ID { get; set; }

    }
}
