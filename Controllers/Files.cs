using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mime;
using System.Threading.Tasks;
using AccountsData.Data;
using AccountsData.Models.DataModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Minio;
using Minio.DataModel;
using File = AccountsData.Models.DataModels.File;

namespace vlo_boards_api.Controllers
{
    [ApiController]
    [Authorize]
    public class FilesController : ControllerBase
    {
        private UserManager<ApplicationUser> userManager;
        private MinioClient minioClient;
        private ApplicationDbContext applicationDbContext;
        private MinioConfig minioConfig;

        public FilesController(UserManager<ApplicationUser> userManager, ApplicationDbContext dbContext, MinioClient minioClient, MinioConfig minioConfig)
        {
            this.userManager = userManager;
            this.applicationDbContext = dbContext;
            this.minioClient = minioClient;
            this.minioConfig = minioConfig;
        }

        [HttpPost]
        [Route("UploadFile")]
        public async Task<IActionResult> OnPost(IFormFile file, bool isPublic)
        {
            try
            {
                ApplicationUser user = await userManager.GetUserAsync(User);
                string id = await user.UploadFile(file, applicationDbContext, minioClient, minioConfig.BucketName, isPublic);
                return Ok(id);
            }
            catch(Exception error)
            {
                Console.WriteLine(error);
                return StatusCode((int) HttpStatusCode.InternalServerError);
            }
            
        }
        
        //rip rest
        [HttpPost]
        [Route("GetFile")]
        public async Task<IActionResult> GetFile(string id)
        {
            ApplicationUser user = await userManager.GetUserAsync(User);
            File file = await applicationDbContext.Files.Where(file => file.ObjectId == id).SingleOrDefaultAsync();
            if (!file.MayView(user))
            {
                return Unauthorized();
            }
            if (file == default(File))
            {
                return NotFound("No file of given id exists");
            }
            var resstream = new MemoryStream();
            await minioClient.GetObjectAsync(minioConfig.BucketName, file.ObjectId, (stream) =>
            {
                stream.CopyTo(resstream);
            });
            
            Response.Headers.Add("Content-Disposition", new ContentDisposition
            {
                FileName = file.FileName,
                Inline = false
            }.ToString());

            return File(resstream.GetBuffer(), file.ContentType);
        }
    }
}