using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Renci.SshNet;
using SkyFy_Api.Services;
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
        public ContentController(DbService dbService, SftpService Sftp)
        {
            _dbService = dbService;
            _sftpService = Sftp;
        }

        //[HttpGet("{id}")]
        //public IActionResult Get(string id) {

        //    var res = _dbService.GetData<ContentClass>("Users");
        //    return Ok(ContentClass.GetContent(id));
        //}

        [HttpGet("{id}/stream")]
        public IActionResult GetStream(long id)
        {
            var remotePath = $"{id}/media.mp3";
            return _sftpService.GetStream(remotePath);
        }



        [HttpPost]
        public IActionResult Create([FromBody] ContentMediaClass content)
        {
            string ID = _dbService.CreateEntity(content as ContentBaseRequest, "Content");

            _sftpService.CreateFolder(ID);         

            return Ok(ID);
        }

        [HttpPost("{id}/file")]
        public IActionResult Upload(long id, [FromBody] IFormFile file)
        {


            _sftpService.UploadFile(file, $"{id}/media.{Path.GetExtension(file.Name)}");


            return Ok();
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

    public class ContentBaseRequest
    {
        public long id;
        public string Name { get; set; }
        public string Cover_Art { get; set; }
        public long User_ID { get; set; }
        //public string Artist_ID { get; set; }
        public string Data { get; set; }
    }
}
