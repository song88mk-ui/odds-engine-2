using System.Collections.Generic;

namespace OE.WebSocket.Service.Messages
{
    public class IncomingVisitFavorites
    {
        public string UserId { get; set; }
        public List<UserFavorites> Favorites { get; set; }

    }
    public class UserFavorites
    {
        public int SportId { get; set; }
        public string SportName { get; set; }
        public int HierarchyId { get; set; }
        public string HierarchyName { get; set; }
        public string Id { get; set; }
        public string Name { get; set; }
    }
}
