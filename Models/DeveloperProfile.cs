using OpenIddict.EntityFrameworkCore.Models;

namespace BeatLeader_Server.Models
{
    public class DeveloperProfile
    {
        public int Id { get; set; }
        public ICollection<OpenIddictEntityFrameworkCoreApplication> OauthApps { get; set; }
    }
}
