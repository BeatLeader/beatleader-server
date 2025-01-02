using Amazon.S3;
using BeatLeader_Server.Extensions;
using BeatLeader_Server.Models;
using BeatLeader_Server.Utils;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BeatLeader_Server.Enums;
using static BeatLeader_Server.Utils.ResponseUtils;
using SixLabors.ImageSharp;
using Swashbuckle.AspNetCore.Annotations;

namespace BeatLeader_Server.Controllers {
    public class MapperController : Controller {
        private readonly AppContext _context;

        IAmazonS3 _s3Client;
        IWebHostEnvironment _environment;

        public MapperController(
            AppContext context,
            IWebHostEnvironment env,
            IConfiguration configuration)
        {
            _context = context;
            _environment = env;
            _s3Client = configuration.GetS3Client();
        }

        [HttpGet("~/mappers/")]
        [SwaggerOperation(Summary = "Retrieve a list of known mappers", Description = "Fetches a paginated and optionally filtered list of mappers.")]
        [SwaggerResponse(200, "List of mappers retrieved successfully", typeof(ResponseWithMetadata<ClanResponseFull>))]
        [SwaggerResponse(400, "Invalid request parameters")]
        [SwaggerResponse(404, "Mappers not found")]
        public async Task<ActionResult<ResponseWithMetadata<MapperResponse>>> GetAll(
            [FromQuery, SwaggerParameter("Page number for pagination, default is 1")] int page = 1,
            [FromQuery, SwaggerParameter("Number of mappers per page, default is 10")] int count = 20,
            [FromQuery] MapperSortBy sort = MapperSortBy.RankedMaps,
            [FromQuery] Order order = Order.Desc,
            [FromQuery] string? search = null,
            [FromQuery] string? ids = null)
        {
            var query = _context.Mappers.TagWithCaller().AsNoTracking();
            if (search != null) {
                query = query.Where(m => m.Name.ToLower().Contains(search.ToLower()));
            }
            if (ids != null) {
                var intIds = ids.Split(",").Select(s => (int?)(int.TryParse(s, out int result) ? result : null)).Where(i => i != null).Distinct().ToList();
                query = query.Where(m => intIds.Contains(m.Id));
            }

            switch (sort) {
                case MapperSortBy.Name:
                    query = query.Order(order, m => m.Name);
                    break;
                case MapperSortBy.Maps:
                    query = query.Order(order, m => m.Songs.Count);
                    break;
                case MapperSortBy.RankedMaps:
                    query = query.Order(order, m => m.Songs.Where(s => s.Difficulties.Any(d => d.Status == DifficultyStatus.ranked)).Count());
                    break;
                default:
                    break;
            }

            return new ResponseWithMetadata<MapperResponse>()
            {
                Metadata = new Metadata()
                {
                    Page = page,
                    ItemsPerPage = count,
                    Total = await query.CountAsync()
                },
                Data = await query
                    .Skip((page - 1) * count)
                    .Take(count)
                    .Select(m => new MapperResponse {
                        Id = m.Id,
                        PlayerId = m.Player != null ? m.Player.Id : null,
                        Name = m.Name,
                        Avatar = m.Avatar,
                        Curator = m.Curator,
                        VerifiedMapper = m.VerifiedMapper
                    })
                    .ToListAsync()
            };
        }
    }
}
