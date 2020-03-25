using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using MiniTwit.Entities;
using MiniTwit.Models;
using MiniTwit.Utils.CustomJson;
using MiniTwit.Web.App.Models.Api;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace MiniTwit.Web.App.Controllers
{
    public class ApiController : Controller
    {
        // TODO: We should put this in a config file
        private const string SimulatorAuthToken = "c2ltdWxhdG9yOnN1cGVyX3NhZmUh";
        
        private readonly ILogger<ApiController> _logger;
        private readonly IMessageRepository _messageRepository;
        private readonly IUserRepository _userRepository;
        private readonly UserManager<User> _userManager;
        private static int _latest;

        public ApiController(
            UserManager<User> userManager, 
            ILogger<ApiController> logger,
            IMessageRepository messageRepository,
            IUserRepository userRepository
        )
        {
            _userManager = userManager;
            _logger = logger;
            _messageRepository = messageRepository;
            _userRepository = userRepository;
        }

        [Route("[controller]/latest/")]
        [HttpGet]
        [AllowAnonymous]
        public IActionResult GetLatest()
        {
            return Json(new {latest = _latest});
        }

        [Route("[controller]/msgs/")]
        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> GetMessages(
            [FromQuery(Name = "latest")] int? latestMessage,
            [FromQuery(Name = "no")] int number = 100
        )
        {
            UpdateLatest(latestMessage);
            if (!IsRequestFromSimulator())
            {
                return NotAuthorizedError();
            }
            var messages = (await _messageRepository.ReadManyAsync(number))
                .Select(GetMessageDTO.FromMessage);
            return CustomJson(messages);
        }

        [Route("[controller]/msgs/{username}")]
        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> UserGetMessages(
            string username,
            [FromQuery(Name = "latest")] int? latestMessage,
            [FromQuery(Name = "no")] int number = 100
        )
        {
            UpdateLatest(latestMessage);
            if (!IsRequestFromSimulator())
            {
                return NotAuthorizedError();
            }
            var user = await _userRepository.ReadAsyncByUsername(username);
            if (user == null)
            {
                LogRequestInfo($"Invalid username '{username}' to get messages for.");
                return NotFound();
            }    
            var messages = (await _messageRepository.ReadManyFromUserAsync(user.Id,number))
                .Select(GetMessageDTO.FromMessage);
            return CustomJson(messages);
        }
        

        [Route("[controller]/msgs/{username}")]
        [HttpPost]
        [AllowAnonymous]
        public async Task<IActionResult> UserPostMessages(
            string username,
            [FromQuery(Name = "latest")] int? latestMessage,
            [FromBody] PostMessageDTO postMessageDto
        )
        {
            UpdateLatest(latestMessage ?? postMessageDto.Latest);
            if (!IsRequestFromSimulator())
            {
                return NotAuthorizedError();
            }
            var user = await _userRepository.ReadAsyncByUsername(username);
            if (user == null || postMessageDto?.Content == null)
            {
                return NotFound();
            }
            await _messageRepository.CreateAsync(postMessageDto.ToMessage(user));
            return StatusCode(204, "");
        }

        [Route("[controller]/register")]
        [HttpPost]
        [AllowAnonymous]
        public async Task<IActionResult> RegisterUser(
            [FromBody] PostRegisterDTO registerPost,
            [FromQuery(Name = "latest")] int? latestMessage
        )
        {
            UpdateLatest(latestMessage ?? registerPost.Latest);
            if (!IsRequestFromSimulator())
            {
                return NotAuthorizedError();
            }
            if (registerPost?.Username == null)
            {
                return StatusCode(400, new {status = 400, error_msg = "You have to enter a username"});
            }
            if (registerPost.Email == null || !registerPost.Email.Contains("@"))
            {
                return StatusCode(400,new {status = 400, error_msg = "You have to enter a valid email address"});
            }
            if (registerPost.Pwd == null)
            {
                return StatusCode(400,new {status = 400, error_msg = "You have to enter a password"});
            }
            if (await _userRepository.ReadAsyncByUsername(registerPost.Username) != null)
            {
                return StatusCode(400,new {status = 400, error_msg = "The username is already taken"});
            }
            var result = await _userManager.CreateAsync(registerPost.ToUser(), registerPost.Pwd);
            if (!result.Succeeded)
            {
                LogRequestInfo($"{registerPost.Username} failed at creation, with result: {result}.");
                return BadRequest(result.Errors);
            }
            LogRequestInfo($"{registerPost.Username} created a new account.");
            return StatusCode(204, "");
        }

        [Route("[controller]/fllws/{username}")]
        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> UserGetFollows(
            string username,
            [FromQuery(Name = "latest")] int? latestMessage,
            [FromQuery(Name = "no")] int number = 100
        )
        {
            UpdateLatest(latestMessage);
            if (!IsRequestFromSimulator())
            {
                return NotAuthorizedError();
            }
            var user = await _userRepository.ReadAsyncByUsername(username);
            var follows = (await _userRepository.GetFollows(user.Id))?
                .Take(number)
                .Select(u => u.UserName);
            return CustomJson(new {follows});
        }

        

        [Route("[controller]/fllws/{username}")]
        [HttpPost]
        [AllowAnonymous]
        public async Task<IActionResult> UserFollow(
            string username,
            [FromQuery(Name = "latest")] int? latestMessage,
            [FromBody] PostFollowDTO postFollowDto
        )
        {
            UpdateLatest(latestMessage ?? postFollowDto.Latest);
            if (!IsRequestFromSimulator())
            {
                return NotAuthorizedError();
            }
            var follower = await _userRepository.ReadAsyncByUsername(username);
            if (follower == null)
            {
                LogRequestInfo($"Invalid follower username '{username}' in follow/unfollow action.");
                return StatusCode(404, "");
            }
            if (postFollowDto?.Follow != null)
            {
                var followee = await _userRepository.ReadAsyncByUsername(postFollowDto.Follow);
                if (followee == null)
                {
                    LogRequestInfo($"Invalid followee username '{postFollowDto.Follow}' in follow operation.");
                    // TODO: This has to be another error, likely 500???
                    return StatusCode(404, "");
                }
                LogRequestInfo($"'{username}' followed '{postFollowDto.Follow}'.");
                await _userRepository.AddFollowerAsync(follower.Id, followee.Id);
            }
            else if (postFollowDto?.UnFollow != null)
            {
                var followee = await _userRepository.ReadAsyncByUsername(postFollowDto.UnFollow);
                if (followee == null)
                {
                    LogRequestInfo($"Invalid followee username '{postFollowDto.UnFollow}' in unfollow operation.");
                    return StatusCode(404, "");
                }
                LogRequestInfo($"'{username}' unfollowed '{postFollowDto.UnFollow}'.");
                await _userRepository.RemoveFollowerAsync(follower.Id, followee.Id);
            }

            return StatusCode(204, "");
        }
        
        private void LogRequestInfo(string message)
        {
            _logger.LogInformation($"Request #{_latest}: {message}");
        }

        private bool IsRequestFromSimulator()
        {
            return HttpContext.Request.Headers["Authorization"].Equals($"Basic ${SimulatorAuthToken}");
        }

        private IActionResult NotAuthorizedError()
        {
            return StatusCode(403, new {status = 403, error_msg = "You are not authorized to use this resource!"});
        }

        private void UpdateLatest(int? latestMessage)
        {
            if (latestMessage != null)
            {
                _latest = latestMessage.Value;
            }
        }

        private IActionResult CustomJson(object data)
        {
            return Json(data, new JsonSerializerSettings { ContractResolver = new CustomJsonResolver() });
        }
    }
}