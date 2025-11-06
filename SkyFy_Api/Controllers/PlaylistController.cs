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
        public IActionResult GetPlaylistContent(long id)
        {
            var result = _dbService.GetEntitiesByField<Playlist_Content>("Playlist_ID", id, "PlaylistContent");
            return Ok(result);
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


        [HttpDelete("remove/content/{playlist_content_id}")]
        [Authorize]
        public IActionResult PlaylistRemoveContent(long playlist_content_id)
        {
            var userId = RequestHelper.GetUserIDFromClaims(User);
            try
            {
                _dbService.DeleteEntity(playlist_content_id, "PlaylistContent");

                return Ok();
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
