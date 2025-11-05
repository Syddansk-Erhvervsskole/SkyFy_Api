using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Rewrite;
using Microsoft.IdentityModel.Tokens;
using SkyFy_Api.Models.Login;
using SkyFy_Api.Services;
using System.Security.Claims;

namespace SkyFy_Api.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class AuthorizationController : ControllerBase
    {
        private readonly EncryptionService _es;
        private readonly TokenService _jwt;
        private readonly DbService _ds;
        public AuthorizationController(TokenService jwt, EncryptionService es, DbService dbService) {
            _jwt = jwt;
            _es = es;
            _ds = dbService;
        }

        [HttpPost("Login")]
        public IActionResult Login([FromBody] LoginRequest request)
        {
            try
            {
                var sha256Password = _es.Sha256(request.Password);
                var user = _ds.GetEntityByField<UserRequest>("Username", request.Username, "Users");

                if (user == null || sha256Password != user.Password)
                    return Unauthorized(new { message = "Invalid credentials" });

                var token = _jwt.GenerateToken(request.Username, user.ID, "", 24);

                return Ok(new
                {
                    user = new UserReturnRequest
                    {
                        ID = user.ID,
                        Username = user.Username,
                        Email = user.Email,
                        Data = user.Data
                    },
                    token
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        [HttpPost("Refresh")]
        [Authorize]
        public IActionResult Refresh()
        {
            try
            {
                var username = User.FindFirstValue(ClaimTypes.Name);
                var id = long.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
                var role = User.FindFirstValue(ClaimTypes.Role);

                if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(role))
                    return Unauthorized(new { message = "Invalid token claims." });

                var newToken = _jwt.GenerateToken(username, id, role);
                return Ok(new { token = newToken });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Internal server error.", detail = ex.Message });
            }
        }


    }
}
