using System.Collections.Generic;

namespace Coflnet.Sky.Crafts.Models
{
    public class ItemData
    {
        public string itemid { get; set; }
        public string displayname { get; set; }
        public string nbttag { get; set; }
        public int damage { get; set; }
        public List<string> lore { get; set; }
        public Dictionary<string, string> recipe { get; set; }
        public string internalname { get; set; }
        public string clickcommand { get; set; }
        public string modver { get; set; }
        public bool useneucraft { get; set; }
        public string infoType { get; set; }
        public List<string> info { get; set; }
        public string crafttext { get; set; }
    }
}
