using System.Collections.Generic;

namespace app.Models
{
    public class ImageGroup
    {
        public string Key { get; set; }
        public string Label { get; set; }
        public List<string> Items { get; set; } = new();
        public string? CoverPath => Items.Count > 0 ? Items[0] : null;
    }
}
