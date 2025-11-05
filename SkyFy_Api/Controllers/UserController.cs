using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SkyFy_Api.Services;
using System.Text.Json;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace SkyFy_Api.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class UserController : ControllerBase
    {
        private DbService _dbService;
        private EncryptionService _encryptionService;
        public UserController(DbService dbService, EncryptionService es)
        {
            _dbService = dbService;
            _encryptionService = es;
        }

        [HttpPost()]
        public IActionResult Create([FromBody] CreateUserRequest createUserRequest)
        {
            createUserRequest.Password = _encryptionService.Sha256(createUserRequest.Password);
            _dbService.CreateEntity(createUserRequest, "Users");

            return Ok(createUserRequest);
        }

        [HttpDelete("{id}")]
        public IActionResult Delete(long id)
        {
           _dbService.DeleteEntity(id, "Users");

            return Ok($"User with id {id} has been deleted");
        }

        [HttpPut("{id}")]

        public IActionResult Edit(long id, [FromBody] UpdateUserRequest updateUserRequest)
        {
            _dbService.UpdateEntity(id, updateUserRequest, "Users");

            return Ok("User updated");
        }

        [HttpPatch("Password/{id}")]

        public IActionResult UpdatePassword(long id, [FromBody] UserPasswordUpdateRequest passwordUpdateRequest)
        {
            var currentUser = _dbService.GetEntityById<UserRequest>(2, "Users");
            if (currentUser == null)
                return NotFound();

            var hashedPassword = _encryptionService.Sha256(passwordUpdateRequest.Old_Password);

            if(hashedPassword == currentUser.Password)
            {
                var NewPasswordHash = _encryptionService.Sha256(passwordUpdateRequest.New_Password);

                var elem = new
                {
                    Password = NewPasswordHash
                };

                _dbService.UpdateEntity(id, elem, "Users");
            }
            else
            {
                return BadRequest(new
                {
                    message = "Old pasword does not match.",
                });
            }


            return Ok();
        }
    }


    public class UserRequest
    {
        public long ID { get; set; }
        public string Email { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public string Data { get; set; }

    }

    public class UserReturnRequest
    {
        public long ID { get; set; }
        public string Email { get; set; }
        public string Username { get; set; }
        public string Data { get; set; }

    }

    public class CreateUserRequest
    {
        public string Email { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }

    }

    public class UpdateUserRequest
    {
        public string Email { get; set; }
        public string Username { get; set; }
        public JsonDocument Data { get; set; }

    }
    public class UserPasswordUpdateRequest
    {
        public string Old_Password { get; set; }
        public string New_Password { get; set; }
    }
}
